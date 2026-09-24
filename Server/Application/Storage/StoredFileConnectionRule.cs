using Application.Diagnostics.Consistency;
using Domain.Entities.Storage;
using Domain.Rules.Storage;

namespace Application.Storage;

// A file kept, or on its way, in a storage that is now disconnected can no
// longer be read. Disconnecting refuses while such files exist, and since
// 2026-09-24 an upload racing a disconnect is refused by the database; this
// finds rows left from before, or by any path that bypasses both. Read
// only: the files' owner reconnects the storage or moves them.
public sealed class StoredFileConnectionRule(IAppDbContext db)
  : IConsistencyRule
{
  public ConsistencyRuleInfo Info { get; } =
    new(
      "storage.file-on-disconnected-storage",
      1,
      "Storage: FileStore, DisconnectStorageCommand",
      ConsistencyCondition.Violation,
      ConsistencySeverity.Warning,
      "A stored file that is kept or being uploaded is in a connected "
        + "storage.",
      "Reconnect the storage in Settings, or move its files; the file rows "
        + "are kept."
    );

  public async Task<ConsistencyPage> ReadAsync(
    ConsistencyPageRequest request,
    CancellationToken ct
  )
  {
    Guid? after = request.After is null ? null : Guid.Parse(request.After);
    var rows = await db
      .StoredFiles.AsNoTracking()
      .Where(f =>
        f.CompanyId == request.Company
        && f.State != StoredFileStates.Failed
        && f.State != StoredFileStates.Rejected
        && (after == null || f.Id.CompareTo(after.Value) > 0)
        && db.StorageConnections.Any(c =>
          c.Id == f.ConnectionId
          && c.State == StorageConnectionStates.Disconnected
        )
      )
      .OrderBy(f => f.Id)
      .Select(f => new
      {
        f.Id,
        f.ConnectionId,
        f.State,
      })
      .Take(request.Limit + 1)
      .ToListAsync(ct);
    return new(
      [
        .. rows.Take(request.Limit)
          .Select(x => new ConsistencyObservation(
            x.Id.ToString(),
            $"file:{x.State}",
            new Dictionary<string, string>
            {
              ["connectionId"] = x.ConnectionId.ToString(),
              ["fileState"] = x.State,
            }
          )),
      ],
      rows.Count > request.Limit
    );
  }
}
