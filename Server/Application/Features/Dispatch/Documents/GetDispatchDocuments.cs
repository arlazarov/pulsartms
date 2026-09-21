using Application.Models;
using Domain.Models.Execution;

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

public sealed class DownloadDispatchDocumentHandler(
  IAppDbContext db,
  ICurrentUser caller,
  IUserRoleService roles
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
      .Select(x => new DispatchDocumentDownload(
        x.FileName,
        x.ContentType,
        x.Content
      ))
      .SingleOrDefaultAsync(ct);
    return item is null
      ? RequestResponse<DispatchDocumentDownload>.Fail(
        "Document not found.",
        404
      )
      : RequestResponse<DispatchDocumentDownload>.Ok(item);
  }
}
