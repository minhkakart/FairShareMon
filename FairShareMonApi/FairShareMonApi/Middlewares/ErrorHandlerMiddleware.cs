using FairShareMonApi.Attributes;
using FairShareMonApi.Constants;
using FairShareMonApi.Exceptions;
using FairShareMonApi.Models;

namespace FairShareMonApi.Middlewares;

/// <summary>
/// Outermost catch for endpoint execution. Sits deliberately AFTER <c>UseRouting</c> because it
/// inspects endpoint metadata for <see cref="ResponseWrappedAttribute"/>; exceptions from
/// non-wrapped endpoints (or after the response has started) are rethrown to the host.
/// </summary>
public class ErrorHandlerMiddleware(RequestDelegate next, ILogger<ErrorHandlerMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception exception)
        {
            // A cancellation caused by the client aborting the request is not an application
            // error - don't log it as one or write a 500 to a connection nobody is reading.
            if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
                throw;

            if (context.Response.HasStarted || context.GetEndpoint()?.Metadata.GetMetadata<ResponseWrappedAttribute>() is null)
                throw;

            var result = exception switch
            {
                ErrorException errorException => ApiResult.Failure(errorException),
                _ => ApiResult.Failure(ErrorCodes.InternalError, "Đã xảy ra lỗi hệ thống. Vui lòng thử lại sau.")
            };

            if (result.StatusCode >= StatusCodes.Status500InternalServerError)
                logger.LogError(exception, "Unhandled exception while executing {Method} {Path}", context.Request.Method, context.Request.Path);
            else
                logger.LogWarning("ErrorException {Code} for {Method} {Path}: {Message}",
                    (exception as ErrorException)?.Code, context.Request.Method, context.Request.Path, exception.Message);

            context.Response.StatusCode = result.StatusCode;
            await context.Response.WriteAsJsonAsync<object>(result, context.RequestAborted);
        }
    }
}
