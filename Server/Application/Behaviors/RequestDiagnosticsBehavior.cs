using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Application.Models;
using Microsoft.Extensions.Logging;

namespace Application.Behaviors;

public static class RequestMetrics
{
  private static readonly ConcurrentDictionary<string, RequestTiming> Totals =
    new();

  public sealed record RequestTiming(
    long Count,
    long Failed,
    long Cancelled,
    double TotalMs,
    double MaxMs
  );

  public static IReadOnlyDictionary<string, RequestTiming> Snapshot() =>
    new Dictionary<string, RequestTiming>(Totals);

  internal static void Record(string name, string outcome, double elapsed) =>
    Totals.AddOrUpdate(
      name,
      new RequestTiming(
        1,
        outcome == "failed" ? 1 : 0,
        outcome == "cancelled" ? 1 : 0,
        elapsed,
        elapsed
      ),
      (_, old) =>
        new(
          old.Count + 1,
          old.Failed + (outcome == "failed" ? 1 : 0),
          old.Cancelled + (outcome == "cancelled" ? 1 : 0),
          old.TotalMs + elapsed,
          Math.Max(old.MaxMs, elapsed)
        )
    );

  internal static readonly Meter Meter = new("PulsarTms.Application");
  internal static readonly Histogram<double> Duration =
    Meter.CreateHistogram<double>("pulsartms.request.duration", "ms");
}

public sealed class RequestDiagnosticsBehavior<TRequest, TResponse>(
  ILogger<RequestDiagnosticsBehavior<TRequest, TResponse>> logger
) : IPipelineBehavior<TRequest, TResponse>
  where TRequest : notnull
{
  // What every line logged inside this request carries. The trace id ties
  // the request to the HTTP call that started it; the work ids tie it to
  // the truck a dispatcher is asking about.
  private static Dictionary<string, object?> Scope(TRequest request)
  {
    var scope = new Dictionary<string, object?>
    {
      ["Request"] = typeof(TRequest).Name,
      ["TraceId"] = Activity.Current?.TraceId.ToString(),
    };
    if (request is not IAboutWork work)
      return scope;
    if (work.Load is { } load)
      scope["Load"] = load;
    if (work.Truck is { } truck)
      scope["Truck"] = truck;
    return scope;
  }

  public async Task<TResponse> Handle(
    TRequest request,
    RequestHandlerDelegate<TResponse> next,
    CancellationToken cancellationToken
  )
  {
    var started = Stopwatch.GetTimestamp();
    var outcome = "completed";
    using var scope = logger.BeginScope(Scope(request));
    try
    {
      var response = await next();
      if (response is IRequestOutcome { Success: false })
        outcome = "failed";
      return response;
    }
    catch (OperationCanceledException)
      when (cancellationToken.IsCancellationRequested)
    {
      outcome = "cancelled";
      throw;
    }
    catch
    {
      outcome = "failed";
      throw;
    }
    finally
    {
      var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
      RequestMetrics.Record(typeof(TRequest).Name, outcome, elapsed);
      RequestMetrics.Duration.Record(
        elapsed,
        new KeyValuePair<string, object?>("request", typeof(TRequest).Name),
        new KeyValuePair<string, object?>("outcome", outcome)
      );
      if (outcome == "completed" && elapsed >= 1000)
        logger.LogInformation(
          "RequestTiming Request={RequestType} DurationMs={DurationMs} Outcome={Outcome} TraceId={TraceId}",
          typeof(TRequest).Name,
          elapsed,
          outcome,
          Activity.Current?.TraceId.ToString()
        );
    }
  }
}
