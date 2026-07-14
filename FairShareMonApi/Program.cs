using Asp.Versioning;
using DiDecoration.Extensions;
using FairShareMonApi.Attributes.MvcFilters;
using FairShareMonApi.Auth;
using FairShareMonApi.Database;
using FairShareMonApi.HostedServices;
using FairShareMonApi.Middlewares;
using FairShareMonApi.Swagger;
using FluentValidation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
// surfaces ModelState errors in the ApiResult envelope instead.
builder.Services.AddControllers(options => options.Filters.Add<ErrorHandlerFilter>());
builder.Services.Configure<ApiBehaviorOptions>(options => options.SuppressModelStateInvalidFilter = true);
builder.Services.AddHttpContextAccessor();

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

// Database: EF Core 8 + Pomelo, MariaDB 11.7.2, pooled context, split queries.
builder.Services.AddDbContextPool<AppDbContext>(options => options
    .UseMySql(
        builder.Configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is missing."),
        new MariaDbServerVersion(new Version(11, 7, 2)),
        mySqlOptions => mySqlOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)));

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

// Authorization: everything requires an authenticated user unless [AllowAnonymous].
builder.Services.AddAuthorization(options =>
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

// Startup backfill: ensure every existing user has an owner-representative member (idempotent,
// self-healing, no-op when none are missing) - planning/members.md OQ2.
builder.Services.AddHostedService<OwnerRepresentativeBackfillHostedService>();

// Startup backfill: ensure every existing user has the suggested categories with one default
// (idempotent, self-healing, no-op when none are missing) - planning/categories-and-tags.md OQ3.
builder.Services.AddHostedService<SuggestedCategoriesBackfillHostedService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseRouting();
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
