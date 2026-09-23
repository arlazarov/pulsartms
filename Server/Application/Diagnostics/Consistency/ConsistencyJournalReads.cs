using Domain.Entities.Consistency;

namespace Application.Diagnostics.Consistency;

// Bounded journal reads for administrative diagnostics. Events are paged
// by their per-company sequence, which commits in order, so a reader that
// resumes from the last sequence it processed misses none and may safely
// read a page again. Incidents are paged by Id: a stable key, complete for
// the rows that exist; changes to an incident arrive as incident events.
public sealed class ConsistencyJournalReads(IAppDbContext db)
{
  public Task<List<ConsistencyFinding>> OpenAsync(
    Guid company,
    int limit,
    CancellationToken ct
  ) =>
    db
      .ConsistencyFindings.AsNoTracking()
      .Where(x => x.CompanyId == company && x.State == "open")
      .OrderByDescending(x => x.Severity == "critical")
      .ThenBy(x => x.FirstSeenAt)
      .Take(limit)
      .ToListAsync(ct);

  public Task<int> OpenCountAsync(Guid company, CancellationToken ct) =>
    db.ConsistencyFindings.CountAsync(
      x => x.CompanyId == company && x.State == "open",
      ct
    );

  // Up to limit + 1 rows, so the caller can tell whether more follow.
  public Task<List<ConsistencyEvent>> EventsAsync(
    Guid company,
    long after,
    int limit,
    string? kindPrefix,
    CancellationToken ct
  ) =>
    db
      .ConsistencyEvents.AsNoTracking()
      .Where(x =>
        x.CompanyId == company
        && x.Sequence > after
        && (kindPrefix == null || x.Kind.StartsWith(kindPrefix))
      )
      .OrderBy(x => x.Sequence)
      .Take(limit + 1)
      .ToListAsync(ct);

  // Up to limit + 1 rows, so the caller can tell whether more follow.
  public Task<List<ConsistencyIncident>> IncidentsAsync(
    Guid company,
    Guid? after,
    int limit,
    CancellationToken ct
  ) =>
    db
      .ConsistencyIncidents.AsNoTracking()
      .Where(x =>
        x.CompanyId == company
        && (after == null || x.Id.CompareTo(after.Value) > 0)
      )
      .OrderBy(x => x.Id)
      .Take(limit + 1)
      .ToListAsync(ct);
}
