using Application.Features.Execution.Models;
using Application.Models;
using Application.Storage;
using Domain.Entities.Storage;

namespace Application.Features.Dispatch.Documents;

public sealed record GetDispatchDocumentsQuery(Guid DispatchId)
  : IRequest<RequestResponse<IReadOnlyList<DispatchDocumentInfo>>>;

public sealed class GetDispatchDocumentsHandler(
  IAppDbContext db,
  ICurrentUser caller,
  IUserRoleService roles
)
  : IRequestHandler<
    GetDispatchDocumentsQuery,
    RequestResponse<IReadOnlyList<DispatchDocumentInfo>>
  >
{
  public async Task<
    RequestResponse<IReadOnlyList<DispatchDocumentInfo>>
  > Handle(GetDispatchDocumentsQuery request, CancellationToken ct)
  {
    if (await ExecutionCommandSupport.ActorAsync(db, caller, roles, ct) is null)
      return RequestResponse<IReadOnlyList<DispatchDocumentInfo>>.Fail(
        "Access denied.",
        403
      );
    if (!await db.Dispatches.AnyAsync(x => x.Id == request.DispatchId, ct))
      return RequestResponse<IReadOnlyList<DispatchDocumentInfo>>.Fail(
        "Load not found.",
        404
      );
    var documents = await (
      from document in db.DispatchDocuments.AsNoTracking()
      where document.DispatchId == request.DispatchId
      orderby document.RecordedAt descending, document.Id
      select new DispatchDocumentInfo(
        document.Id,
        document.Kind,
        document.FileName,
        document.ContentType,
        document.Length,
        document.RecordedAt,
        document.ActorName
      )
    )
      .Take(DispatchDocumentRules.MaximumCount)
      .ToListAsync(ct);
    return RequestResponse<IReadOnlyList<DispatchDocumentInfo>>.Ok(documents);
  }
}

public sealed record DownloadDispatchDocumentQuery(
  Guid DispatchId,
  Guid DocumentId
) : IRequest<RequestResponse<DispatchDocumentDownload>>;

// A document filed from a driver's message is read from its stored file,
// and only while that file is released; the bytes are bounded by the
// document limit, which filing enforces.
public sealed class DownloadDispatchDocumentHandler(
  IAppDbContext db,
  ICurrentUser caller,
  IUserRoleService roles,
  FileStore files
)
  : IRequestHandler<
    DownloadDispatchDocumentQuery,
    RequestResponse<DispatchDocumentDownload>
  >
{
  public async Task<RequestResponse<DispatchDocumentDownload>> Handle(
    DownloadDispatchDocumentQuery request,
    CancellationToken ct
  )
  {
    if (await ExecutionCommandSupport.ActorAsync(db, caller, roles, ct) is null)
      return RequestResponse<DispatchDocumentDownload>.Fail(
        "Access denied.",
        403
      );
    var item = await db
      .DispatchDocuments.AsNoTracking()
      .Where(x =>
        x.Id == request.DocumentId && x.DispatchId == request.DispatchId
      )
      .Select(x => new
      {
        x.FileName,
        x.ContentType,
        x.Content,
        x.StoredFileId,
      })
      .SingleOrDefaultAsync(ct);
    if (item is null)
      return NotFound();
    if (item.StoredFileId is not { } stored)
      return RequestResponse<DispatchDocumentDownload>.Ok(
        new(item.FileName, item.ContentType, item.Content)
      );
    (StoredFile File, Stream Content)? opened;
    try
    {
      opened = await files.OpenAsync(stored, quarantined: false, ct);
    }
    catch (StorageUnavailableException)
    {
      return RequestResponse<DispatchDocumentDownload>.Fail(
        "The file's storage is not answering. Please retry.",
        503
      );
    }
    if (opened is not { } found)
      return NotFound();
    await using var content = found.Content;
    // The stream ends at the recorded length, which is checked first, so
    // the buffer is sized and bounded before anything is read.
    if (found.File.Size > DispatchDocumentRules.MaximumBytes)
      return NotFound();
    var bytes = new byte[found.File.Size];
    try
    {
      await content.ReadExactlyAsync(bytes, ct);
    }
    catch (StorageContentMismatchException)
    {
      return NotFound();
    }
    return RequestResponse<DispatchDocumentDownload>.Ok(
      new(item.FileName, item.ContentType, bytes)
    );

    static RequestResponse<DispatchDocumentDownload> NotFound() =>
      RequestResponse<DispatchDocumentDownload>.Fail(
        "Document not found.",
        404
      );
  }
}
