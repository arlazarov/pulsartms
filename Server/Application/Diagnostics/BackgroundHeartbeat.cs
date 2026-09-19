using System.Collections.Concurrent;

namespace Application.Diagnostics;

// Which background work has run recently, so the platform can tell a process
// that is answering HTTP but has stopped doing anything from one that is
// working.
//
// This exists because an instance died and nobody learned of it for seven
// and a half hours - the owner noticed a missing fuel stop, which is four
// layers away from the cause. The liveness endpoint answered "alive" the
// whole time, because it checked nothing.
//
// Deliberately in memory and not in the database: a liveness answer must not
// depend on the database, or a brief connection problem restarts a container
// that was fine.
public static class BackgroundHeartbeat
{
  private static readonly ConcurrentDictionary<string, DateTimeOffset> Beats =
    new(StringComparer.Ordinal);
  private static readonly ConcurrentDictionary<string, TimeSpan> Expected = new(
    StringComparer.Ordinal
  );

  // Called once when an operation starts, naming how long a quiet stretch is
  // normal for it. Only operations that registered are ever required to
  // report, so an instance running a narrowed set of roles is not judged on
  // work it was told not to do.
  public static void Expect(string operation, TimeSpan interval)
  {
    Expected[operation] = interval;
    Beats[operation] = DateTimeOffset.UtcNow;
  }

  // Called when a cycle finishes, whether it did work or found none. A
  // handled failure still counts: the loop is alive, which is what this
  // measures. Only a loop that has stopped turning goes quiet.
  public static void Beat(string operation) =>
    Beats[operation] = DateTimeOffset.UtcNow;

  public static void Forget(string operation)
  {
    Expected.TryRemove(operation, out _);
    Beats.TryRemove(operation, out _);
  }

  // Operations that have not reported for far longer than their own cycle.
  // The multiplier is generous on purpose: this decides whether to restart a
  // running container, and a restart loop is worse than a slow cycle.
  public static IReadOnlyList<string> Stalled(
    DateTimeOffset now,
    int tolerance = 10
  ) =>
    [
      .. Expected
        .Where(x =>
          Beats.TryGetValue(x.Key, out var last)
          && now - last > Multiply(x.Value, tolerance)
        )
        .Select(x => x.Key)
        .Order(StringComparer.Ordinal),
    ];

  private static TimeSpan Multiply(TimeSpan interval, int tolerance)
  {
    // Never less than five minutes, so a one-second loop does not restart the
    // process over a ten-second stall.
    var scaled = interval * tolerance;
    return scaled < TimeSpan.FromMinutes(5) ? TimeSpan.FromMinutes(5) : scaled;
  }
}
