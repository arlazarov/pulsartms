using System.Text.Json;
using Domain.Entities.Consistency;

namespace Application.Diagnostics.Consistency;

// The durable record of what the auditor saw and did, for one company and
// one unit of work. Each write is one transaction that takes the company's
// journal lock before it reads any finding state, decides on fresh rows,
// assigns journal sequences and commits before the caller moves on. Writers
// of one company are therefore serialized end to end: a stale pass cannot
// resolve a finding observed after it read, roll back LastSeenAt or lose a
// repair attempt, and sequences commit in order.
//
// A journal whose save failed holds changes that were never written. It is
// not reused: the auditor disposes its whole unit of work and opens another.
public sealed partial class ConsistencyJournal(IAppDbContext db)
{
  public const int ResolveLimit = 500;

  public Task<IReadOnlyList<ConsistencyEvent>> RecordAsync(
    Guid company,
    Guid passId,
    ConsistencyRuleInfo rule,
    DateTime at,
    IReadOnlyList<ConsistencyObservation> observed,
    CancellationToken ct
  )
  {
    if (observed.Count == 0)
      return Task.FromResult<IReadOnlyList<ConsistencyEvent>>([]);
    return SerializedAsync(
      company,
      async events =>
      {
        var keys = observed.Select(x => x.EntityKey).Distinct().ToArray();
        var open = await db
          .ConsistencyFindings.Where(x =>
            x.CompanyId == company
            && x.Rule == rule.Id
            && x.State == "open"
            && keys.Contains(x.EntityKey)
          )
          .ToDictionaryAsync(x => x.EntityKey, StringComparer.Ordinal, ct);
        var fresh = keys.Where(x => !open.ContainsKey(x)).ToArray();
        var previous =
          fresh.Length == 0
            ? []
            : await db
              .ConsistencyFindings.AsNoTracking()
              .Where(x =>
                x.CompanyId == company
                && x.Rule == rule.Id
                && fresh.Contains(x.EntityKey)
              )
              .GroupBy(x => x.EntityKey)
              .Select(x => new { x.Key, Occurrence = x.Max(f => f.Occurrence) })
              .ToDictionaryAsync(x => x.Key, x => x.Occurrence, ct);
        var incidents = await IncidentsAsync(
          company,
          rule.Id,
          previous.Keys,
          ct
        );
        foreach (var seen in observed.DistinctBy(x => x.EntityKey))
        {
          if (open.TryGetValue(seen.EntityKey, out var finding))
          {
            // An older observation says nothing new; an unchanged one is not a
            // transition and writes no event.
            if (at >= finding.LastSeenAt)
            {
              finding.LastSeenAt = at;
              finding.Versions = seen.Versions;
              finding.EvidenceJson = Evidence(seen.Evidence);
            }
            continue;
          }
          finding = new ConsistencyFinding
          {
            Id = Guid.NewGuid(),
            CompanyId = company,
            Rule = rule.Id,
            RuleVersion = rule.Version,
            EntityKey = seen.EntityKey,
            Occurrence = previous.GetValueOrDefault(seen.EntityKey) + 1,
            State = "open",
            Condition = ConsistencyWords.Of(rule.Condition),
            Severity = ConsistencyWords.Of(rule.Severity),
            Versions = seen.Versions,
            EvidenceJson = Evidence(seen.Evidence),
            FirstSeenAt = at,
            LastSeenAt = at,
            LastTransitionAt = at,
          };
          db.ConsistencyFindings.Add(finding);
          events.Add(Event(finding, "opened", passId, at, []));
          // Back after a verified resolution: the fix did not hold.
          if (finding.Occurrence > 1)
            events.Add(Incident(incidents, finding, "recurred", passId, at));
        }
        return events;
      },
      ct
    );
  }

  // A complete sweep that began after these findings were last seen did not
  // see them: they are resolved by that authoritative re-read, not by any
  // repair having been requested.
  public Task<IReadOnlyList<ConsistencyEvent>> ResolveAsync(
    Guid company,
    Guid passId,
    string rule,
    ConsistencySweeps.Completion sweep,
    DateTime at,
    CancellationToken ct
  )
  {
    return SerializedAsync(
      company,
      async events =>
      {
        var stale = await db
          .ConsistencyFindings.Where(x =>
            x.CompanyId == company
            && x.Rule == rule
            && x.State == "open"
            && x.LastSeenAt < sweep.StartedAt
          )
          .OrderBy(x => x.LastSeenAt)
          .Take(ResolveLimit)
          .ToListAsync(ct);
        foreach (
          var finding in stale.Where(x => !sweep.Seen.Contains(x.EntityKey))
        )
        {
          finding.State = "resolved";
          finding.ResolvedAt = at;
          finding.LastTransitionAt = at;
          events.Add(
            Event(
              finding,
              "resolved",
              passId,
              at,
              new()
              {
                ["verifiedBy"] = "complete-sweep",
                ["sweepStartedAt"] = sweep.StartedAt.ToString("O"),
                ["repairAttempts"] = finding.RepairAttempts.ToString(),
              }
            )
          );
        }
        return events;
      },
      ct
    );
  }

  // One serialized journal write. Tracking is cleared first so the decision
  // reads committed rows under the lock, not values an earlier operation of
  // this unit loaded before another writer changed them.
  private async Task<IReadOnlyList<ConsistencyEvent>> SerializedAsync(
    Guid company,
    Func<List<ConsistencyEvent>, Task<List<ConsistencyEvent>>> decide,
    CancellationToken ct
  )
  {
    if (db.ChangeTracker.HasChanges())
      throw new InvalidOperationException(
        "A journal operation began with unsaved changes."
      );
    db.ChangeTracker.Clear();
    await using var transaction = await db.Database.BeginTransactionAsync(ct);
    await db.LockConsistencyJournalAsync(company, ct);
    var events = await decide([]);
    if (events.Count > 0)
    {
      var head = await db.ConsistencyJournalHeads.SingleOrDefaultAsync(
        x => x.CompanyId == company,
        ct
      );
      if (head is null)
        db.ConsistencyJournalHeads.Add(head = new() { CompanyId = company });
      foreach (var entry in events)
      {
        if (entry.CompanyId != company)
          throw new InvalidOperationException(
            "A journal write belongs to one company."
          );
        entry.Sequence = ++head.LastSequence;
        db.ConsistencyEvents.Add(entry);
      }
    }
    await db.SaveChangesAsync(ct);
    await transaction.CommitAsync(ct);
    return events;
  }

  private Task<ConsistencyFinding?> FindingAsync(
    Guid company,
    Guid id,
    CancellationToken ct
  ) =>
    db.ConsistencyFindings.SingleOrDefaultAsync(
      x => x.CompanyId == company && x.Id == id,
      ct
    );

  private Task<Dictionary<string, ConsistencyIncident>> IncidentsAsync(
    Guid company,
    string rule,
    IEnumerable<string> keys,
    CancellationToken ct
  )
  {
    var wanted = keys.ToArray();
    return wanted.Length == 0
      ? Task.FromResult(
        new Dictionary<string, ConsistencyIncident>(StringComparer.Ordinal)
      )
      : db
        .ConsistencyIncidents.Where(x =>
          x.CompanyId == company
          && x.Rule == rule
          && wanted.Contains(x.EntityKey)
        )
        .ToDictionaryAsync(x => x.EntityKey, StringComparer.Ordinal, ct);
  }

  private ConsistencyEvent Incident(
    Dictionary<string, ConsistencyIncident> incidents,
    ConsistencyFinding finding,
    string reason,
    Guid passId,
    DateTime at
  )
  {
    var opened = !incidents.TryGetValue(finding.EntityKey, out var incident);
    if (opened)
    {
      incident = new ConsistencyIncident
      {
        Id = Guid.NewGuid(),
        CompanyId = finding.CompanyId,
        Rule = finding.Rule,
        EntityKey = finding.EntityKey,
        OpenedAt = at,
      };
      db.ConsistencyIncidents.Add(incident);
      incidents[finding.EntityKey] = incident;
    }
    incident!.Reason = reason;
    incident.Recurrences = finding.Occurrence - 1;
    incident.LastFindingId = finding.Id;
    incident.LastAt = at;
    return Event(
      finding,
      opened ? "incident-opened" : "incident-updated",
      passId,
      at,
      new()
      {
        ["incidentId"] = incident.Id.ToString(),
        ["reason"] = reason,
        ["occurrence"] = finding.Occurrence.ToString(),
      }
    );
  }

  private static ConsistencyEvent Event(
    ConsistencyFinding finding,
    string kind,
    Guid passId,
    DateTime at,
    Dictionary<string, string> detail
  ) =>
    new()
    {
      CompanyId = finding.CompanyId,
      FindingId = finding.Id,
      Kind = kind,
      Rule = finding.Rule,
      EntityKey = finding.EntityKey,
      Occurrence = finding.Occurrence,
      PassId = passId,
      At = at,
      DetailJson = JsonSerializer.Serialize(detail),
    };

  // Evidence is a handful of IDs and versions by construction; anything
  // larger is dropped rather than stored in part.
  private static string Evidence(IReadOnlyDictionary<string, string> evidence)
  {
    var json = JsonSerializer.Serialize(evidence);
    return json.Length <= 4000 ? json : "{\"truncated\":\"true\"}";
  }
}
