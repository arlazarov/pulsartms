using System.Data;
using System.Linq.Expressions;
using Application.Features.Execution.Models;
using Application.Models;
using Domain.Entities.Dispatch;
using Domain.Entities.Storage;

namespace Application.Features.Dispatch.Documents;

// A dispatcher files a file a driver sent to a load, as one of its
// documents. Only a dispatcher's confirmation files: nothing is filed from
// a suggestion. The document refers to the stored file, which must have
// passed its check, and copies no bytes. Everything is read and checked
// inside the committing transaction, and the file's row is claimed there
// on the state that was checked. Filing the same attachment to the
// same load again returns the document already filed; a load of another
// company is not found.
public sealed record FileMessageAttachmentCommand(
  Guid AttachmentId,
  Guid? DispatchId,
  int? LoadNumber,
  string Kind
) : IRequest<RequestResponse<DispatchDocumentInfo>>;

public sealed class FileMessageAttachmentHandler(
  IAppDbContext db,
  ICurrentUser caller,
  IUserRoleService roles,
  TimeProvider clock
)
  : IRequestHandler<
    FileMessageAttachmentCommand,
    RequestResponse<DispatchDocumentInfo>
  >
{
  public static readonly string[] Types =
  [
    "application/pdf",
    "image/png",
    "image/jpeg",
  ];

  public async Task<RequestResponse<DispatchDocumentInfo>> Handle(
    FileMessageAttachmentCommand command,
    CancellationToken ct
  )
  {
    var actor = await ExecutionCommandSupport.ActorAsync(db, caller, roles, ct);
    if (actor is null)
      return Fail("Access denied.", 403);
    if (!DispatchDocumentRules.ValidKind(command.Kind))
      return Fail("Choose a document type.", 400);
    try
    {
      await using var transaction = await db.Database.BeginTransactionAsync(
        IsolationLevel.Serializable,
        ct
      );
      var dispatch = await DispatchAsync(command, ct);
      if (dispatch is not { } dispatchId)
        return Fail("Load not found.", 404);
      var file = await db
        .MessageAttachments.AsNoTracking()
        .Where(x => x.Id == command.AttachmentId && x.StoredFileId != null)
        .Join(
          db.StoredFiles.AsNoTracking(),
          attachment => attachment.StoredFileId,
          stored => stored.Id,
          (attachment, stored) =>
            new
            {
              stored.Id,
              stored.State,
              stored.ContentType,
              stored.Size,
              stored.Sha256,
              Name = attachment.OriginalName != ""
                ? attachment.OriginalName
                : stored.Name,
            }
        )
        .SingleOrDefaultAsync(ct);
      if (file is null)
        return Fail("File not found.", 404);
      if (file.State != StoredFileStates.Available)
        return Fail("This file has not passed its check yet.", 409);
      if (
        !Types.Contains(file.ContentType)
        || file.Size > DispatchDocumentRules.MaximumBytes
      )
        return Fail(
          "Only a PDF, PNG or JPEG up to 5 MB can be filed to a load.",
          400
        );
      var filed = await db
        .DispatchDocuments.AsNoTracking()
        .Where(x =>
          x.DispatchId == dispatchId
          && x.SourceAttachmentId == command.AttachmentId
        )
        .Select(Info)
        .SingleOrDefaultAsync(ct);
      if (filed is not null)
        return RequestResponse<DispatchDocumentInfo>.Ok(filed);
      if (
        await db.DispatchDocuments.CountAsync(
          x => x.DispatchId == dispatchId,
          ct
        ) >= DispatchDocumentRules.MaximumCount
      )
        return Fail("This load has reached its 50-document limit.", 409);
      var document = new DispatchDocument
      {
        Id = Guid.NewGuid(),
        DispatchId = dispatchId,
        Kind = command.Kind,
        FileName = DispatchDocumentRules.FileName(file.Name, file.ContentType),
        ContentType = file.ContentType,
        Content = [],
        Length = (int)file.Size,
        ContentHash = file.Sha256.ToUpperInvariant(),
        RecordedAt = clock.GetUtcNow().UtcDateTime,
        RecordedBy = actor.Value,
        ActorName = await db
          .Users.Where(x => x.Id == actor.Value)
          .Select(x => x.Name)
          .SingleAsync(ct),
        StoredFileId = file.Id,
        SourceAttachmentId = command.AttachmentId,
      };
      // The check above was a read. Claiming the file's row, on the
      // condition that it is still released and unchanged, holds it until
      // this commit: a quarantine or removal that committed after the read
      // stops the filing here, and one that comes later waits for it.
      var held = await db
        .StoredFiles.Where(x =>
          x.Id == file.Id
          && x.State == StoredFileStates.Available
          && x.Sha256 == file.Sha256
        )
        .ExecuteUpdateAsync(x => x.SetProperty(f => f.State, f => f.State), ct);
      if (held != 1)
        return Fail("This file changed while it was being filed.", 409);
      db.DispatchDocuments.Add(document);
      await db.SaveChangesAsync(ct);
      await transaction.CommitAsync(ct);
      return RequestResponse<DispatchDocumentInfo>.Ok(
        new(
          document.Id,
          document.Kind,
          document.FileName,
          document.ContentType,
          document.Length,
          document.RecordedAt,
          document.ActorName
        )
      );
    }
    catch (Exception ex) when (db.IsWriteConflict(ex))
    {
      // Another confirmation filed it first: that document stands.
      return Fail("This file was just filed. Refresh to see it.", 409);
    }
  }

  // The load chosen from the suggestions by id, or typed by its number,
  // which is unique within the company.
  private async Task<Guid?> DispatchAsync(
    FileMessageAttachmentCommand command,
    CancellationToken ct
  )
  {
    if (command.DispatchId is { } id)
      return await db.Dispatches.AnyAsync(x => x.Id == id, ct) ? id : null;
    if (command.LoadNumber is not { } number)
      return null;
    return await db
      .Dispatches.Where(x => x.LoadNumber == number)
      .Select(x => (Guid?)x.Id)
      .SingleOrDefaultAsync(ct);
  }

  private static readonly Expression<
    Func<DispatchDocument, DispatchDocumentInfo>
  > Info = x => new DispatchDocumentInfo(
    x.Id,
    x.Kind,
    x.FileName,
    x.ContentType,
    x.Length,
    x.RecordedAt,
    x.ActorName
  );

  private static RequestResponse<DispatchDocumentInfo> Fail(
    string message,
    int status
  ) => RequestResponse<DispatchDocumentInfo>.Fail(message, status);
}
