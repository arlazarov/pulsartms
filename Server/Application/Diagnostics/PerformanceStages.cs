using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Application.Diagnostics;

// Stages were recorded into meters with no listener and no exporter, so
// nothing could read them: the instrumentation existed and the measurements
// did not. Totals are now kept the way RequestMetrics keeps its own, which is
// what `GET /api/diagnostics/stages` reads, and the meters stay for whatever
// collector is attached later.
public static class PerformanceStages
{
  private static readonly Meter Meter = new("PulsarTms.Performance");
  private static readonly Histogram<double> Duration =
    Meter.CreateHistogram<double>("pulsartms.stage.duration", "ms");
  private static readonly Histogram<long> Work = Meter.CreateHistogram<long>(
    "pulsartms.stage.items",
    "items"
  );

  private static readonly ConcurrentDictionary<string, StageTiming> Totals =
    new(StringComparer.Ordinal);

  public sealed record StageTiming(
    long Count,
    double TotalMs,
    double MaxMs,
    long Items
  );

  public static IReadOnlyDictionary<string, StageTiming> Snapshot() =>
    new Dictionary<string, StageTiming>(Totals, StringComparer.Ordinal);

  public static Scope Start(string operation, string stage) =>
    new(operation, stage);

  public static void Elapsed(string operation, string stage, long started) =>
    Record(
      operation,
      stage,
      Stopwatch.GetElapsedTime(started).TotalMilliseconds
    );

  public static void Count(string operation, string stage, long count)
  {
    Work.Record(
      count,
      new KeyValuePair<string, object?>("operation", operation),
      new KeyValuePair<string, object?>("stage", stage)
    );
    Totals.AddOrUpdate(
      $"{operation}/{stage}",
      new StageTiming(0, 0, 0, count),
      (_, old) => old with { Items = old.Items + count }
    );
  }

  public static void Record(string operation, string stage, double elapsed)
  {
    Duration.Record(
      elapsed,
      new KeyValuePair<string, object?>("operation", operation),
      new KeyValuePair<string, object?>("stage", stage)
    );
    Totals.AddOrUpdate(
      $"{operation}/{stage}",
      new StageTiming(1, elapsed, elapsed, 0),
      (_, old) =>
        old with
        {
          Count = old.Count + 1,
          TotalMs = old.TotalMs + elapsed,
          MaxMs = Math.Max(old.MaxMs, elapsed),
        }
    );
  }

  public readonly struct Scope(string operation, string stage) : IDisposable
  {
    private readonly long started = Stopwatch.GetTimestamp();

    public void Dispose() =>
      Record(
        operation,
        stage,
        Stopwatch.GetElapsedTime(started).TotalMilliseconds
      );
  }
}
