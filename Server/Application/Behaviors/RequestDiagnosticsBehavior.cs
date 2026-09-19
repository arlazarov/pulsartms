using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;

namespace Application.Behaviors;

public static class RequestMetrics
{
  private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Application.Models.RequestTiming> Totals = new();
  public static IReadOnlyDictionary<string, Application.Models.RequestTiming> Snapshot() => new Dictionary<string, Application.Models.RequestTiming>(Totals);
  internal static void Record(string name, string outcome, double elapsed) => Totals.AddOrUpdate(name,
    new Application.Models.RequestTiming(1, outcome == "failed" ? 1 : 0, outcome == "cancelled" ? 1 : 0, elapsed, elapsed),
    (_, old) => new(old.Count + 1, old.Failed + (outcome == "failed" ? 1 : 0),
      old.Cancelled + (outcome == "cancelled" ? 1 : 0), old.TotalMs + elapsed, Math.Max(old.MaxMs, elapsed)));
  internal static readonly Meter Meter = new("AMFTMS.Application");
  internal static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("amftms.request.duration", "ms");
}

public sealed class RequestDiagnosticsBehavior<TRequest, TResponse>(ILogger<RequestDiagnosticsBehavior<TRequest, TResponse>> logger)
  : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
  public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
  {
    var started = Stopwatch.GetTimestamp();
    var outcome = "completed";
    try
    {
      var response = await next();
      if (response is Application.Models.IRequestOutcome { Success: false }) outcome = "failed";
      return response;
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { outcome = "cancelled"; throw; }
    catch { outcome = "failed"; throw; }
    finally
    {
      var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
      RequestMetrics.Record(typeof(TRequest).Name, outcome, elapsed);
      RequestMetrics.Duration.Record(elapsed, new KeyValuePair<string, object?>("request", typeof(TRequest).Name),
        new KeyValuePair<string, object?>("outcome", outcome));
      if (outcome == "completed" && elapsed >= 1000)
        logger.LogInformation("RequestTiming Request={RequestType} DurationMs={DurationMs} Outcome={Outcome} TraceId={TraceId}",
          typeof(TRequest).Name, elapsed, outcome, Activity.Current?.TraceId.ToString());
    }
  }
}
