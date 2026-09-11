using System.Diagnostics;
using Application.Behaviors;

namespace Application.Features.Routing.Services;

internal static class GateWait
{
  internal static async Task WaitAsync(SemaphoreSlim gate, string operation, CancellationToken ct)
  {
    var started = Stopwatch.GetTimestamp();
    var outcome = "completed";
    try { await gate.WaitAsync(ct); }
    catch (OperationCanceledException) { outcome = "cancelled"; throw; }
    finally
    {
      var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
      RequestMetrics.Record("GateWait:" + operation, outcome, elapsed);
      RequestMetrics.Duration.Record(elapsed, new KeyValuePair<string, object?>("request", "GateWait:" + operation),
        new KeyValuePair<string, object?>("outcome", outcome));
    }
  }
}
