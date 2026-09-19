using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Application.Diagnostics;

public static class PerformanceStages
{
  private static readonly Meter Meter = new("PulsarTms.Performance");
  private static readonly Histogram<double> Duration =
    Meter.CreateHistogram<double>("pulsartms.stage.duration", "ms");
  private static readonly Histogram<long> Work = Meter.CreateHistogram<long>(
    "pulsartms.stage.items",
    "items"
  );

  public static Scope Start(string operation, string stage) =>
    new(operation, stage);

  public static void Elapsed(string operation, string stage, long started) =>
    Duration.Record(
      Stopwatch.GetElapsedTime(started).TotalMilliseconds,
      new KeyValuePair<string, object?>("operation", operation),
      new KeyValuePair<string, object?>("stage", stage)
    );

  public static void Count(string operation, string stage, long count) =>
    Work.Record(
      count,
      new KeyValuePair<string, object?>("operation", operation),
      new KeyValuePair<string, object?>("stage", stage)
    );

  public readonly struct Scope(string operation, string stage) : IDisposable
  {
    private readonly long started = Stopwatch.GetTimestamp();

    public void Dispose() =>
      Duration.Record(
        Stopwatch.GetElapsedTime(started).TotalMilliseconds,
        new KeyValuePair<string, object?>("operation", operation),
        new KeyValuePair<string, object?>("stage", stage)
      );
  }
}
