using Asp.Versioning;
using DiDecoration.Extensions;
using FairShareMonApi.Attributes.MvcFilters;
using FairShareMonApi.Auth;
using FairShareMonApi.Database;
using FairShareMonApi.Extensions;
using FairShareMonApi.Middlewares;
using FairShareMonApi.Serialization;
using FairShareMonApi.Swagger;
using FluentValidation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.OpenApi.Models;
using NLog;
using NLog.Extensions.Logging;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Logging: NLog, configured from the "NLog" appsettings section.
LogManager.Configuration = new NLogLoggingConfiguration(builder.Configuration.GetSection("NLog"));
builder.Logging.ClearProviders();
builder.Logging.AddNLog();

// MVC: global error filter; the built-in invalid-model filter is suppressed so ErrorHandlerFilter
// surfaces ModelState errors in the ApiResult envelope instead. The UTC-aware DateTime converters
// present every DateTime in the request zone (X-Time-Zone header -> app-default) while storage stays
// UTC (planning/timezone-aware-datetimes.md). HttpContextAccessor is a stateless AsyncLocal wrapper,
// so a fresh instance here reads the same per-request HttpContext the middleware populates.
builder.Services.AddControllers(options => options.Filters.Add<ErrorHandlerFilter>());
// The UTC-aware DateTime converters emit localized parse errors (planning/localization-subsystem.md D4),
// so they need an IStringLocalizerFactory resolved from DI. Configure MVC's JsonOptions through a
// DI-aware options configurator rather than the inline AddJsonOptions lambda.
builder.Services
    .AddOptions<Microsoft.AspNetCore.Mvc.JsonOptions>()
    .Configure<IStringLocalizerFactory>((options, localizerFactory) =>
    {
        var httpContextAccessor = new HttpContextAccessor();
        options.JsonSerializerOptions.Converters.Add(new UtcAwareDateTimeConverter(httpContextAccessor, builder.Configuration, localizerFactory));
        options.JsonSerializerOptions.Converters.Add(new UtcAwareNullableDateTimeConverter(httpContextAccessor, builder.Configuration, localizerFactory));
    });
builder.Services.Configure<ApiBehaviorOptions>(options => options.SuppressModelStateInvalidFilter = true);
builder.Services.AddHttpContextAccessor();
// Runtime message localization: IStringLocalizer<StringResources> over the resx family (neutral vi-VN +
// en-US satellite). Culture is resolved per request by UseAppLocalization below.
builder.Services.AddAppLocalization();

// Attribute-driven DI (DiDecoration): [ScopedService] / [SingletonService] / [TransientService].
builder.Services.RegisterDecorators(builder.Configuration, typeof(Program).Assembly);

// AutoMapper (pinned 13.0.1) + FluentValidation validators (MANUAL validation only - services
// inject IValidator<T> and validate explicitly; no auto-validation).
builder.Services.AddAutoMapper(typeof(Program).Assembly);
builder.Services.AddValidatorsFromAssembly(typeof(Program).Assembly);

// API versioning: routes are api/v{version}/[controller].
builder.Services
    .AddApiVersioning(options =>
    {
        options.DefaultApiVersion = new ApiVersion(1, 0);
        options.AssumeDefaultVersionWhenUnspecified = true;
        options.ReportApiVersions = true;
    })
    .AddMvc()
    .AddApiExplorer(options =>
    {
        options.GroupNameFormat = "'v'VVV";
        options.SubstituteApiVersionInUrl = true;
    });

// Swagger (Bearer scheme for the opaque token).
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "FairShareMon API",
        Version = "v1",
        Description = "API sổ ghi nợ chi tiêu - quản lý phiếu chi tiêu, phần gánh và công nợ theo đợt."
    });
    options.EnableAnnotations();
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        In = ParameterLocation.Header,
        Description = "Nhập access token (opaque token) nhận được sau khi đăng nhập."
    });
    // Padlock per-operation (guarded endpoints only) instead of a document-wide requirement.
    options.OperationFilter<AuthorizeOperationFilter>();
});

// Database: EF Core 8 + Pomelo, MariaDB 11.7.2, pooled context, split queries. The session-pinning
// interceptor forces every connection to UTC (SET time_zone = '+00:00') so DB-generated UpdatedAt is
// true UTC regardless of the server session zone (planning/timezone-aware-datetimes.md).
builder.Services.AddDbContextPool<AppDbContext>((serviceProvider, options) => options
    .UseMySql(
        builder.Configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is missing."),
        new MariaDbServerVersion(new Version(11, 7, 2)),
        mySqlOptions => mySqlOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
    .AddInterceptors(serviceProvider.GetRequiredService<UtcSessionTimeZoneInterceptor>()));

// Redis (StackExchange.Redis) - future token-whitelist cache. AbortOnConnectFail = false so the
// app boots even when Redis is unreachable; the multiplexer reconnects in the background.
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    var redisConfiguration = ConfigurationOptions.Parse(
        builder.Configuration.GetValue<string>("Redis:Configuration") ?? "localhost:6379");
    redisConfiguration.AbortOnConnectFail = false;
    return ConnectionMultiplexer.Connect(redisConfiguration);
});

// Authentication: opaque stateful token as the default scheme (validation is delegated to
// ITokenValidator - whitelist lookup, Redis cache-first with auth_tokens DB fallback).
builder.Services
    .AddAuthentication(OpaqueTokenAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, OpaqueTokenAuthenticationHandler>(
        OpaqueTokenAuthenticationHandler.SchemeName, null);

// Authorization: everything requires an authenticated user unless [AllowAnonymous]; the "Admin"
// policy (M11) additionally requires the role claim == ADMIN. A non-admin fails it and gets the
// already-wired 403 Forbidden 1004 (OpaqueTokenAuthenticationHandler.HandleForbiddenAsync).
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
    options.AddPolicy(FairShareMonApi.Constants.AuthorizationPolicies.Admin, policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim(FairShareMonApi.Constants.AuthorizationPolicies.RoleClaimType, FairShareMonApi.Constants.UserRoles.Admin));
});

// Startup backfills (owner-representative members, suggested categories) are [BackgroundService]-
// annotated and registered by the DiDecoration RegisterDecorators scan above - see
// planning/hosted-service-di-registration.md. No manual AddHostedService here.

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseRouting();
// Resolve the request culture (?culture= -> Accept-Language -> app-default) into CurrentUICulture before
// any endpoint, filter, validator, or JSON converter runs, so IStringLocalizer resolves per request.
// Placed beside RequestTimeZoneMiddleware and before ErrorHandlerMiddleware (planning/localization-subsystem.md).
app.UseAppLocalization(app.Configuration);
// Resolve the request timezone (X-Time-Zone header -> app-default) into HttpContext.Items early, so the
// singleton JSON converters and scoped IRequestTimeZone both read one resolution. No auth dependency.
app.UseMiddleware<RequestTimeZoneMiddleware>();
// After UseRouting on purpose: the middleware needs endpoint metadata to see [ResponseWrapped].
app.UseMiddleware<ErrorHandlerMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

// Exposes the entry point to WebApplicationFactory in FairShareMonApi.Tests.
public partial class Program
{
}
