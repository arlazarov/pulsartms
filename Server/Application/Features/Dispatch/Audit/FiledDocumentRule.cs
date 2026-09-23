using Application.Diagnostics.Consistency;
using Domain.Entities.Storage;

namespace Application.Features.Dispatch.Audit;

// A load document filed from a driver's message has no bytes of its own:
// it opens only while its stored file exists and is released. A file that
// went missing or was quarantined again leaves a document that cannot be
// opened. Read only; the file's owner decides what happens to it.
public sealed class FiledDocumentRule(IAppDbContext db) : IConsistencyRule
{
  public ConsistencyRuleInfo Info { get; } =
    new(
      "dispatch.filed-document-unavailable",
      1,
      "Dispatch: FileMessageAttachment",
      ConsistencyCondition.Violation,
      ConsistencySeverity.Warning,
      "A filed load document's stored file exists and is released.",
      "Check the stored file's state and storage connection; the message "
        + "attachment still holds the same file."
    );

  public async Task<ConsistencyPage> ReadAsync(
    ConsistencyPageRequest request,
    CancellationToken ct
  )
  {
    Guid? after = request.After is null ? null : Guid.Parse(request.After);
    var rows = await db
      .DispatchDocuments.AsNoTracking()
      .Where(d =>
        d.CompanyId == request.Company
        && d.StoredFileId != null
        && (after == null || d.Id.CompareTo(after.Value) > 0)
        && !db.StoredFiles.Any(f =>
          f.Id == d.StoredFileId && f.State == StoredFileStates.Available
        )
      )
      .OrderBy(d => d.Id)
      .Select(d => new
      {
        d.Id,
        d.DispatchId,
        d.StoredFileId,
        State = db
          .StoredFiles.Where(f => f.Id == d.StoredFileId)
          .Select(f => f.State)
          .FirstOrDefault(),
      })
      .Take(request.Limit + 1)
      .ToListAsync(ct);
    return new(
      [
        .. rows.Take(request.Limit)
          .Select(x => new ConsistencyObservation(
            x.Id.ToString(),
            $"file:{x.State ?? "none"}",
            new Dictionary<string, string>
            {
              ["dispatchId"] = x.DispatchId.ToString(),
              ["storedFileId"] = x.StoredFileId.ToString()!,
              ["fileState"] = x.State ?? "none",
            }
          )),
      ],
      rows.Count > request.Limit
    );
  }
}
