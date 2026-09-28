using System.Collections.Concurrent;

namespace Application.Diagnostics;

// Which background operations are making progress, for people to read.
// Unlike BackgroundHeartbeat nothing here fails liveness: with one instance
// a restart is an outage, and a slow round is not a dead loop (root's
// decision). Stale says "no round started since" or "a round running
// since", never "failed".
//
// A periodic operation is stale when no round started for Tolerance times
// its interval (at least Minimum). An operation that runs on demand - a
// queue, a viewer's request - may rightly wait for ever, so it is stale
// only while a round runs longer than its own limit; with rounds
// overlapping, from the start of the first still counted as running.
public static class BackgroundProgress
{
  public const int Tolerance = 3;
  public static readonly TimeSpan Minimum = TimeSpan.FromMinutes(5);

  public sealed record Progress(
    string Operation,
    bool Periodic,
    TimeSpan Limit,
    DateTimeOffset? LastStarted,
    DateTimeOffset? LastFinished,
    int Running,
    bool Stale
  );

  private sealed class Entry(
    bool periodic,
    TimeSpan limit,
    DateTimeOffset since
  )
  {
    public bool Periodic { get; } = periodic;
    public TimeSpan Limit { get; } = limit;
    public DateTimeOffset Since { get; } = since;
    public DateTimeOffset? Started { get; set; }
    public DateTimeOffset? Finished { get; set; }
    public DateTimeOffset? OldestRunning { get; set; }
    public int Running { get; set; }
  }

  private static readonly ConcurrentDictionary<string, Entry> Entries = new(
    StringComparer.Ordinal
  );

  // Called once when a periodic operation starts, with its own cadence.
  public static void Expect(string operation, TimeSpan interval)
  {
    var scaled = interval * Tolerance;
    Entries[operation] = new(
      true,
      scaled < Minimum ? Minimum : scaled,
      DateTimeOffset.UtcNow
    );
  }

  // Called once when an on-demand operation starts, with the longest a
  // round of it normally takes.
  public static void OnDemand(string operation, TimeSpan longestRound) =>
    Entries[operation] = new(false, longestRound, DateTimeOffset.UtcNow);

  public static void Started(string operation) =>
    Started(operation, DateTimeOffset.UtcNow);

  public static void Started(string operation, DateTimeOffset now)
  {
    if (!Entries.TryGetValue(operation, out var entry))
      return;
    lock (entry)
    {
      entry.Started = now;
      // A periodic round is counted by its start alone.
      if (!entry.Periodic && entry.Running++ == 0)
        entry.OldestRunning = now;
    }
  }

  public static void Finished(string operation) =>
    Finished(operation, DateTimeOffset.UtcNow);

  public static void Finished(string operation, DateTimeOffset now)
  {
    if (!Entries.TryGetValue(operation, out var entry))
      return;
    lock (entry)
    {
      entry.Finished = now;
      if (entry.Running > 0 && --entry.Running == 0)
        entry.OldestRunning = null;
    }
  }

  public static void Forget(string operation) =>
    Entries.TryRemove(operation, out _);

  public static IReadOnlyList<Progress> Read(DateTimeOffset now) =>
    [
      .. Entries
        .OrderBy(x => x.Key, StringComparer.Ordinal)
        .Select(x =>
        {
          var e = x.Value;
          lock (e)
          {
            var stale = e.Periodic
              ? now - (e.Started ?? e.Since) > e.Limit
              : e.OldestRunning is { } oldest && now - oldest > e.Limit;
            return new Progress(
              x.Key,
              e.Periodic,
              e.Limit,
              e.Started,
              e.Finished,
              e.Running,
              stale
            );
          }
        }),
    ];
}
