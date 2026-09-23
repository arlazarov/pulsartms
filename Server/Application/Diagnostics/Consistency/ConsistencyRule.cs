namespace Application.Diagnostics.Consistency;

public enum ConsistencyCondition
{
  Violation,
  Review,
}

public enum ConsistencySeverity
{
  Warning,
  Critical,
}

// One invariant, checked by the module that owns it. A rule reads one
// key-ordered page of the serving company's rows that break it, in a single
// statement, so a page is one coherent snapshot. It never calls a provider,
// rebuilds geometry or changes state; repair stays with the owner.
public interface IConsistencyRule
{
  ConsistencyRuleInfo Info { get; }

  Task<ConsistencyPage> ReadAsync(
    ConsistencyPageRequest request,
    CancellationToken ct
  );
}

public sealed record ConsistencyRuleInfo(
  string Id,
  int Version,
  string Owner,
  ConsistencyCondition Condition,
  ConsistencySeverity Severity,
  string Invariant,
  string OwnerAction
);

// After is the last key of the previous page; a rule returns keys in the
// order its query sorts them, and compares After in the same query.
public sealed record ConsistencyPageRequest(
  Guid Company,
  DateTime Now,
  string? After,
  int Limit,
  TimeSpan PendingGrace
);

// EntityKey is both the page cursor and the occurrence identity. Versions is
// the dependency vector the finding was observed at; Evidence is a few IDs,
// revisions and states, never provider payloads or personal data.
public sealed record ConsistencyObservation(
  string EntityKey,
  string Versions,
  IReadOnlyDictionary<string, string> Evidence
);

public sealed record ConsistencyPage(
  IReadOnlyList<ConsistencyObservation> Observed,
  bool More
);
