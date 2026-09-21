using System.Data;
using Application.Models;
using Domain.Entities.Dispatch;
using Domain.Models.Execution;

namespace Application.Features.Dispatch.Documents;

public sealed record UploadDispatchDocumentCommand(
  Guid DispatchId,
  UploadDispatchDocumentRequest Request
) : IRequest<RequestResponse<DispatchDocumentInfo>>;

public sealed class UploadDispatchDocumentHandler(
  IAppDbContext db,
  ICurrentUser caller,
  IUserRoleService roles,
  TimeProvider clock
)
  : IRequestHandler<
    UploadDispatchDocumentCommand,
    RequestResponse<DispatchDocumentInfo>
  >
{
  public async Task<RequestResponse<DispatchDocumentInfo>> Handle(
    UploadDispatchDocumentCommand command,
    CancellationToken ct
  )
  {
    var actor = await ExecutionCommandSupport.ActorAsync(db, caller, roles, ct);
    if (actor is null)
      return Fail("Access denied.", 403);
    var request = command.Request;
    var type = DispatchDocumentRules.ContentType(request?.Content);
    if (
      request is null
      || request.IdempotencyKey == Guid.Empty
      || !DispatchDocumentRules.ValidKind(request.Kind)
      || type is null
    )
      return Fail(
        "Choose a PDF, PNG or JPEG up to 5 MB and a document type.",
        400
      );
    var name = DispatchDocumentRules.FileName(request.FileName, type);
    var hash = DispatchDocumentRules.Hash(request.Content);
    try
    {
      await using var transaction = await db.Database.BeginTransactionAsync(
        IsolationLevel.Serializable,
        ct
      );
      if (!await db.Dispatches.AnyAsync(x => x.Id == command.DispatchId, ct))
        return Fail("Load not found.", 404);
      var saved = await db
        .DispatchDocuments.AsNoTracking()
        .Where(x => x.Id == request.IdempotencyKey)
        .Select(x => new
        {
          x.Id,
          x.DispatchId,
          x.RecordedBy,
          x.ActorName,
          x.ContentHash,
          x.FileName,
          x.Kind,
          x.ContentType,
          x.Length,
          x.RecordedAt,
        })
        .SingleOrDefaultAsync(ct);
      if (saved is not null)
        return
          saved.DispatchId == command.DispatchId
          && saved.RecordedBy == actor
          && saved.ContentHash == hash
          && saved.FileName == name
          && saved.Kind == request.Kind
          ? RequestResponse<DispatchDocumentInfo>.Ok(
            new(
              saved.Id,
              saved.Kind,
              saved.FileName,
              saved.ContentType,
              saved.Length,
              saved.RecordedAt,
              saved.ActorName
            )
          )
          : Fail("This retry key belongs to a different upload.", 409);
      if (
        await db.DispatchDocuments.CountAsync(
          x => x.DispatchId == command.DispatchId,
          ct
        ) >= DispatchDocumentRules.MaximumCount
      )
        return Fail("This load has reached its 50-document limit.", 409);
      var document = new DispatchDocument
      {
        Id = request.IdempotencyKey,
        DispatchId = command.DispatchId,
        Kind = request.Kind,
        FileName = name,
        ContentType = type,
        Content = request.Content,
        Length = request.Content.Length,
        ContentHash = hash,
        RecordedAt = clock.GetUtcNow().UtcDateTime,
        RecordedBy = actor.Value,
        ActorName = await db
          .Users.Where(x => x.Id == actor.Value)
          .Select(x => x.Name)
          .SingleAsync(ct),
      };
      db.DispatchDocuments.Add(document);
      await db.SaveChangesAsync(ct);
      await transaction.CommitAsync(ct);
      return RequestResponse<DispatchDocumentInfo>.Ok(Info(document));
    }
    catch (Exception ex) when (db.IsWriteConflict(ex))
    {
      return Fail("Another upload completed. Retry this same upload.", 409);
    }
  }

  private static DispatchDocumentInfo Info(DispatchDocument document) =>
    new(
      document.Id,
      document.Kind,
      document.FileName,
      document.ContentType,
      document.Length,
      document.RecordedAt,
      document.ActorName
    );

  private static RequestResponse<DispatchDocumentInfo> Fail(
    string message,
    int status
  ) => RequestResponse<DispatchDocumentInfo>.Fail(message, status);
}
