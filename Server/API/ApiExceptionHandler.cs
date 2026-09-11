using Application.Models;
using Microsoft.AspNetCore.Diagnostics;

namespace API;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
  public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception,
    CancellationToken cancellationToken)
  {
    if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
    {
      context.Response.StatusCode = 499;
      return true;
    }
    logger.LogError(exception, "Request failed: {Method} {Path}", context.Request.Method, context.Request.Path);
    context.Response.StatusCode = exception is HttpRequestException ? 502 : 500;
    await context.Response.WriteAsJsonAsync(RequestResponse<object>.Fail(
      exception is HttpRequestException ? "The external service is unavailable. Please try again."
      : "The request could not be completed. Please try again.", context.Response.StatusCode), cancellationToken);
    return true;
  }
}
