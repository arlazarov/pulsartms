namespace Application.Diagnostics.Consistency;

// A safe, targeted action through the owner of a rule's data, such as making
// stuck demand available again. It must re-check the versions it was given,
// be idempotent and never change physical execution, assignments, history
// or send anything outside. Its outcome is a request, not a repair: only a
// later sweep that no longer observes the finding resolves it.
public interface IConsistencyRepair
{
  string Rule { get; }
  string Action { get; }

  Task<string> RequestAsync(
    ConsistencyRepairRequest request,
    CancellationToken ct
  );
}

public sealed record ConsistencyRepairRequest(
  Guid Company,
  string EntityKey,
  IReadOnlyDictionary<string, string> Evidence,
  DateTime Now
);

public static class ConsistencyRepairOutcome
{
  public const string Requested = "requested";
  public const string Superseded = "superseded";
  public const string Failed = "failed";
}
