using FairShareMonApi.Extensions;
using FairShareMonApi.Middlewares;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Unit tests for <see cref="CorsOriginLoggingMiddleware"/>. The middleware exists to make a rejected
/// preflight audible - <c>CorsMiddleware</c> answers every preflight with a bare 204 whether the origin
/// passed or not - so these tests pin exactly when it speaks and prove it never alters the response.
/// </summary>
public class CorsOriginLoggingMiddlewareTests
{
    private const string AllowedOrigin = "https://fairsharemon.minhkakart.com";

    [Fact]
    public async Task NoOriginHeader_LogsNothing()
    {
        // Same-origin request: CORS never applies.
        var result = await InvokeAsync(origin: null, [AllowedOrigin]);

        Assert.Empty(result.Logger.Entries);
    }

    [Fact]
    public async Task AllowedOrigin_LogsNothing()
    {
        var result = await InvokeAsync(AllowedOrigin, [AllowedOrigin]);

        Assert.Empty(result.Logger.Entries);
    }

    [Fact]
    public async Task HttpVariantOfAllowedHttpsOrigin_LogsWarningNamingTheOrigin()
    {
        // THE regression: the origin comparison includes the scheme, so a page served over plaintext
        // fails an https-only allowlist. This is what silently broke login on Safari.
        var result = await InvokeAsync("http://fairsharemon.minhkakart.com", [AllowedOrigin]);

        var entry = Assert.Single(result.Logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("http://fairsharemon.minhkakart.com", entry.Message);
    }

    [Fact]
    public async Task UnknownOrigin_LogsWarning()
    {
        var result = await InvokeAsync("https://evil.example", [AllowedOrigin]);

        var entry = Assert.Single(result.Logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
    }

    [Fact]
    public async Task NoOriginsConfigured_LogsWarningWithoutThrowing()
    {
        var result = await InvokeAsync(AllowedOrigin, []);

        Assert.Single(result.Logger.Entries);
    }

    [Theory]
    [InlineData(true, 0)]  // Development auto-allows localhost, matching the policy predicate.
    [InlineData(false, 1)] // Production does not.
    public async Task LocalOrigin_TracksTheSameDevelopmentGateAsThePolicy(bool isDevelopment, int expectedEntries)
    {
        var result = await InvokeAsync("http://localhost:5173", [AllowedOrigin], isDevelopment);

        Assert.Equal(expectedEntries, result.Logger.Entries.Count);
    }

    [Fact]
    public async Task RejectedOrigin_StillCallsNextAndLeavesTheResponseUntouched()
    {
        // UseCors remains the sole authority over the response; this middleware only observes.
        var result = await InvokeAsync("http://fairsharemon.minhkakart.com", [AllowedOrigin]);

        Assert.True(result.NextCalled);
        Assert.Equal(StatusCodes.Status200OK, result.Context.Response.StatusCode);
        Assert.Empty(result.Context.Response.Headers);
    }

    private static async Task<InvocationResult> InvokeAsync(
        string? origin,
        string[] allowedOrigins,
        bool isDevelopment = false)
    {
        var nextCalled = false;

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(allowedOrigins.Select((value, index) =>
                new KeyValuePair<string, string?>($"{CorsExtensions.AllowedOriginsConfigKey}:{index}", value)))
            .Build();

        var environment = new StubWebHostEnvironment
        {
            EnvironmentName = isDevelopment ? Environments.Development : Environments.Production
        };

        var logger = new CapturingLogger<CorsOriginLoggingMiddleware>();

        var middleware = new CorsOriginLoggingMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            configuration,
            environment,
            logger);

        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Options;
        context.Request.Path = "/api/v1/auth/login";

        if (origin is not null)
            context.Request.Headers[HeaderNames.Origin] = origin;

        await middleware.InvokeAsync(context);

        return new InvocationResult(logger, context, nextCalled);
    }

    private sealed record InvocationResult(
        CapturingLogger<CorsOriginLoggingMiddleware> Logger,
        DefaultHttpContext Context,
        bool NextCalled);

    /// <summary>Records the rendered message of every log call, so tests can assert on level + content.</summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    private sealed class StubWebHostEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = nameof(FairShareMonApi);
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
