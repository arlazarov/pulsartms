using Application.Models;
using Application.Storage;
using Domain.Entities.Storage;

namespace Application.Features.Routing.Queries;

// A message's file, only once it passed its check: a quarantined or refused
// file is never served. The caller disposes the content.
public sealed record GetAttachmentContentQuery(Guid Id)
  : IRequest<RequestResponse<AttachmentContent>>;

public sealed record AttachmentContent(
  Stream Content,
  string ContentType,
  string Name
);

public sealed class AttachmentContentHandler(IAppDbContext db, FileStore files)
  : IRequestHandler<
    GetAttachmentContentQuery,
    RequestResponse<AttachmentContent>
  >
{
  public async Task<RequestResponse<AttachmentContent>> Handle(
    GetAttachmentContentQuery request,
    CancellationToken ct
  )
  {
    var stored = await db
      .MessageAttachments.AsNoTracking()
      .Where(x => x.Id == request.Id)
      .Select(x => x.StoredFileId)
      .SingleOrDefaultAsync(ct);
    var opened = stored is { } id ? await OpenAsync(id, ct) : null;
    return opened is { } found
      ? RequestResponse<AttachmentContent>.Ok(
        new(found.Content, found.File.ContentType, found.File.Name)
      )
      : RequestResponse<AttachmentContent>.Fail(
        "This file is not available.",
        404
      );
  }

  private async Task<(StoredFile File, Stream Content)?> OpenAsync(
    Guid id,
    CancellationToken ct
  )
  {
    try
    {
      return await files.OpenAsync(id, quarantined: false, ct);
    }
    catch (StorageUnavailableException)
    {
      return null;
    }
  }
}
