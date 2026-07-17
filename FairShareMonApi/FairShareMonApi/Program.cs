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
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.OpenApi.Models;
using NLog;
using NLog.Extensions.Logging;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Configuration: CreateBuilder already loads appsettings.json + appsettings.{env}.json, then
// environment variables and command-line args. Append an optional, gitignored per-environment local
// override (appsettings.{env}.local.json), then re-add environment variables and command-line args so
// they win over that file. Final precedence (low -> high): appsettings.json < appsettings.{env}.json <
// appsettings.{env}.local.json < environment variables < command-line args — so container/deploy-time
// env vars (e.g. ConnectionStrings__Default) override the local file.
builder.Configuration
    .AddJsonFile(
        $"appsettings.{builder.Environment.EnvironmentName}.local.json",
        optional: true,
        reloadOnChange: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args);

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

// CORS: single "DefaultCors" policy. Configured origins (App:AllowedOrigins) are honored in every
// environment; localhost/loopback/private origins are auto-allowed ONLY in Development
// (planning/cors-configuration.md). Bearer token lives in the Authorization header, so credentialed
// CORS via SetIsOriginAllowed + AllowCredentials is safe.
builder.Services.AddDefaultCorsPolicy(builder.Configuration, builder.Environment.IsDevelopment());

// Forwarded headers: process X-Forwarded-For / X-Forwarded-Proto so the app sees the real client IP
// and scheme behind the reverse proxy (nginx). Without this configuration UseForwardedHeaders() is a
// no-op (the default ForwardedHeaders is None). KnownNetworks/KnownProxies are cleared because the API
// is only reachable over the internal container network (compose `expose`, never published), so the
// immediate peer is always the trusted proxy.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

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

// Apply pending EF Core migrations at startup when explicitly enabled (App:RunMigrationsOnStartup).
// OFF by default so tests and local runs are unaffected and boot still tolerates an unreachable DB;
// the deployment sets App__RunMigrationsOnStartup=true so `docker compose up` brings the schema
// current from the migrations compiled into the assembly. Single-writer: enable on one api instance
// only (EF has no cross-instance migration lock for MySQL) — run migrations before scaling out.
if (app.Configuration.GetValue<bool>("App:RunMigrationsOnStartup"))
{
    using var migrationScope = app.Services.CreateScope();
    var migrationLogger = migrationScope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var migrationDb = migrationScope.ServiceProvider.GetRequiredService<AppDbContext>();
    migrationLogger.LogInformation("Applying database migrations on startup...");
    migrationDb.Database.Migrate();
    migrationLogger.LogInformation("Database migrations applied.");
}

// Forwarded headers must run first so the request scheme/remote IP are rewritten from the proxy's
// X-Forwarded-* headers before any middleware reads them (options configured above).
app.UseForwardedHeaders();

if (!app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseRouting();
// CORS must run after routing and before authentication/authorization so preflight and
// cross-origin responses carry the Access-Control-* headers (planning/cors-configuration.md).
app.UseCors(CorsExtensions.DefaultCorsPolicyName);
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
