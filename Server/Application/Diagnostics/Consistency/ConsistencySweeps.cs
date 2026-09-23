namespace Application.Diagnostics.Consistency;

// Where each rule's sweep stands, per company, in this process. Findings and
// their history are durable in ConsistencyJournal; this is only the cursor
// and coverage, so after a restart every rule reports unknown coverage until
// it has swept again, and nothing already recorded is lost.
public sealed class ConsistencySweeps
{
  private readonly object gate = new();
  private readonly Dictionary<(Guid, string), State> states = [];

  public sealed record Completion(
    DateTime StartedAt,
    IReadOnlySet<string> Seen
  );

  private sealed class State
  {
    public string? Cursor;
    public DateTime? StartedAt;
    public HashSet<string> Seen = new(StringComparer.Ordinal);
    public int Rows;
    public int LastSweepRows;
    public DateTime? LastPassAt;
    public DateTime? LastCompleteSweepAt;
    public string? LastError;
    public DateTime? LastErrorAt;
    public bool Failed;
  }

  // The cursor the next page reads from; begins a sweep when none is open.
  public string? BeginPage(Guid company, string rule, DateTime at)
  {
    lock (gate)
    {
      var state = Get(company, rule);
      if (state.StartedAt is null)
      {
        state.StartedAt = at;
        state.Cursor = null;
        state.Seen.Clear();
        state.Rows = 0;
      }
      return state.Cursor;
    }
  }

  // Called only after the page was recorded. Returns the finished sweep when
  // this was its last page.
  public Completion? Advance(
    Guid company,
    string rule,
    DateTime at,
    ConsistencyPage page
  )
  {
    lock (gate)
    {
      var state = Get(company, rule);
      state.LastPassAt = at;
      state.Failed = false;
      state.StartedAt ??= at;
      foreach (var seen in page.Observed)
        state.Seen.Add(seen.EntityKey);
      state.Rows += page.Observed.Count;
      if (page.Observed.Count > 0)
        state.Cursor = page.Observed[^1].EntityKey;
      if (page.More)
        return null;
      var completion = new Completion(
        state.StartedAt.Value,
        state.Seen.ToHashSet(StringComparer.Ordinal)
      );
      state.LastCompleteSweepAt = at;
      state.LastSweepRows = state.Rows;
      state.StartedAt = null;
      state.Cursor = null;
      state.Seen.Clear();
      return completion;
    }
  }

  // Coverage is unknown and the same page is read again.
  public void Fail(Guid company, string rule, DateTime at, string error)
  {
    lock (gate)
    {
      var state = Get(company, rule);
      state.LastPassAt = at;
      state.Failed = true;
      state.LastError = error;
      state.LastErrorAt = at;
    }
  }

  public IReadOnlyList<ConsistencyRuleCoverage> Coverage(
    Guid company,
    IEnumerable<ConsistencyRuleInfo> rules,
    DateTime now,
    TimeSpan staleAfter
  )
  {
    lock (gate)
      return
      [
        .. rules
          .OrderBy(x => x.Id, StringComparer.Ordinal)
          .Select(rule =>
          {
            states.TryGetValue((company, rule.Id), out var state);
            var status =
              state?.LastPassAt is null ? "never-run"
              : state.Failed ? "failed"
              : state.LastCompleteSweepAt is not { } complete
              || now - complete > staleAfter
                ? "stale"
              : state.StartedAt is not null ? "sweeping"
              : "complete";
            return new ConsistencyRuleCoverage(
              rule.Id,
              rule.Version,
              rule.Owner,
              ConsistencyWords.Of(rule.Condition),
              rule.Invariant,
              status,
              state?.LastPassAt,
              state?.LastCompleteSweepAt,
              state?.LastSweepRows ?? 0,
              state?.StartedAt is null ? 0 : state.Rows,
              state?.LastError,
              state?.LastErrorAt
            );
          }),
      ];
  }

  private State Get(Guid company, string rule)
  {
    if (!states.TryGetValue((company, rule), out var state))
      states[(company, rule)] = state = new();
    return state;
  }
}

public static class ConsistencyWords
{
  public static string Of(ConsistencyCondition value) =>
    value == ConsistencyCondition.Violation ? "violation" : "review";

  public static string Of(ConsistencySeverity value) =>
    value == ConsistencySeverity.Critical ? "critical" : "warning";
}

public sealed record ConsistencyRuleCoverage(
  string Rule,
  int Version,
  string Owner,
  string Condition,
  string Invariant,
  string Coverage,
  DateTime? LastPassAt,
  DateTime? LastCompleteSweepAt,
  int LastSweepRows,
  int CurrentSweepRows,
  string? LastError,
  DateTime? LastErrorAt
);
