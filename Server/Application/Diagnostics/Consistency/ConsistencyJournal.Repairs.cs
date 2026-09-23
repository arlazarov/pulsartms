using Domain.Entities.Consistency;

namespace Application.Diagnostics.Consistency;

// Repair attempts and escalation. Each step re-reads its finding under the
// journal lock, so a candidate list read earlier never decides on its own.
public sealed partial class ConsistencyJournal
{
  // Candidates only: every repair step re-reads its finding under the lock.
  public Task<List<ConsistencyFinding>> RepairableAsync(
    Guid company,
    IReadOnlyCollection<string> rules,
    DateTime cooledBefore,
    int limit,
    CancellationToken ct
  ) =>
    db
      .ConsistencyFindings.AsNoTracking()
      .Where(x =>
        x.CompanyId == company
        && x.State == "open"
        && !x.Escalated
        && rules.Contains(x.Rule)
        && (x.LastRepairAt == null || x.LastRepairAt <= cooledBefore)
      )
      .OrderBy(x => x.FirstSeenAt)
      .Take(limit)
      .ToListAsync(ct);

  // Counts the attempt and saves it as "pending" before the owner is asked,
  // so a crash mid-repair leaves the attempt counted, the cooldown running
  // and the outcome visibly unknown. Null when the finding is no longer
  // eligible: resolved, escalated, cooling down or out of attempts.
  public async Task<ConsistencyFinding?> BeginRepairAsync(
    Guid company,
    Guid findingId,
    string action,
    Guid passId,
    DateTime at,
    DateTime cooledBefore,
    int maxAttempts,
    CancellationToken ct
  )
  {
    ConsistencyFinding? begun = null;
    await SerializedAsync(
      company,
      async events =>
      {
        var finding = await FindingAsync(company, findingId, ct);
        if (
          finding is not { State: "open", Escalated: false }
          || finding.LastRepairAt > cooledBefore
          || finding.RepairAttempts >= maxAttempts
        )
          return events;
        finding.RepairAttempts++;
        finding.LastRepairAt = at;
        finding.LastRepairOutcome = "pending";
        events.Add(
          Event(
            finding,
            "repair-requested",
            passId,
            at,
            new()
            {
              ["action"] = action,
              ["attempt"] = finding.RepairAttempts.ToString(),
              ["versions"] = finding.Versions,
            }
          )
        );
        begun = finding;
        return events;
      },
      ct
    );
    return begun;
  }

  // The outcome belongs to one attempt; a later attempt's state is kept.
  public Task EndRepairAsync(
    Guid company,
    Guid findingId,
    int attempt,
    string outcome,
    Guid passId,
    DateTime at,
    CancellationToken ct
  ) =>
    SerializedAsync(
      company,
      async events =>
      {
        var finding = await FindingAsync(company, findingId, ct);
        if (finding is null)
          return events;
        if (finding.RepairAttempts == attempt)
          finding.LastRepairOutcome = outcome;
        events.Add(
          Event(
            finding,
            "repair-" + outcome,
            passId,
            at,
            new() { ["attempt"] = attempt.ToString() }
          )
        );
        return events;
      },
      ct
    );

  // Repair did not settle it within its attempts: stop retrying and hand it
  // to a person as an incident. False when it no longer qualifies.
  public async Task<bool> EscalateAsync(
    Guid company,
    Guid findingId,
    int maxAttempts,
    Guid passId,
    DateTime at,
    CancellationToken ct
  )
  {
    var escalated = false;
    await SerializedAsync(
      company,
      async events =>
      {
        var finding = await FindingAsync(company, findingId, ct);
        if (
          finding is not { State: "open", Escalated: false }
          || finding.RepairAttempts < maxAttempts
        )
          return events;
        finding.Escalated = true;
        finding.LastTransitionAt = at;
        var incidents = await IncidentsAsync(
          company,
          finding.Rule,
          [finding.EntityKey],
          ct
        );
        events.Add(
          Event(
            finding,
            "escalated",
            passId,
            at,
            new() { ["attempts"] = finding.RepairAttempts.ToString() }
          )
        );
        events.Add(
          Incident(incidents, finding, "repair-exhausted", passId, at)
        );
        escalated = true;
        return events;
      },
      ct
    );
    return escalated;
  }
}
