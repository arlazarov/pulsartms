using Application.Models;
using Domain.Entities.Storage;

namespace Application.Storage;

// Opens the provider's folder picker for one connection. The answer carries
// a short-lived access token for the administrator's browser only.
public sealed record GetStoragePickerCommand(Guid Id)
  : IRequest<RequestResponse<StoragePickerSession>>;

public sealed record ChooseStorageRootCommand(
  Guid Id,
  string FolderId,
  long ExpectedRevision
) : IRequest<RequestResponse<StorageConnectionView>>;

// Where a connected account's files go. The folder is whatever the
// administrator picked, verified through this connection's own grant: it
// must be a folder this account can add files to. Choosing again later moves
// only new files; stored files stay where they were written.
public sealed class StorageRoots(
  IAppDbContext db,
  FileStore files,
  IEnumerable<IStorageRootPicker> pickers,
  TimeProvider clock
)
  : IRequestHandler<
    GetStoragePickerCommand,
    RequestResponse<StoragePickerSession>
  >,
    IRequestHandler<
      ChooseStorageRootCommand,
      RequestResponse<StorageConnectionView>
    >
{
  public async Task<RequestResponse<StoragePickerSession>> Handle(
    GetStoragePickerCommand request,
    CancellationToken ct
  )
  {
    var connection = await Choosable(request.Id, ct);
    if (connection is null)
      return RequestResponse<StoragePickerSession>.Fail(
        "This connection cannot choose a folder.",
        409
      );
    var picker = pickers.FirstOrDefault(x => x.Kind == connection.Kind);
    if (picker is not { IsConfigured: true })
      return RequestResponse<StoragePickerSession>.Fail(
        "The server has no folder picker registration for this provider.",
        409
      );
    var session = await picker.SessionAsync(files.Target(connection), ct);
    return session is null
      ? RequestResponse<StoragePickerSession>.Fail(
        "The provider refused access. Connect again.",
        409
      )
      : RequestResponse<StoragePickerSession>.Ok(session);
  }

  public async Task<RequestResponse<StorageConnectionView>> Handle(
    ChooseStorageRootCommand request,
    CancellationToken ct
  )
  {
    var connection = await Choosable(request.Id, ct, tracked: true);
    if (connection is null)
      return Fail("This connection cannot choose a folder.", 409);
    if (connection.Revision != request.ExpectedRevision)
      return Fail("The connection changed. Reload and try again.", 409);
    var picker = pickers.FirstOrDefault(x => x.Kind == connection.Kind);
    var root = picker is null
      ? null
      : await picker.VerifyAsync(
        files.Target(connection),
        request.FolderId ?? "",
        ct
      );
    if (root is null)
      return Fail(
        "PulsR cannot add files to that folder. Pick another one.",
        409
      );
    connection.Root = root.Id;
    connection.RootName = root.Name;
    connection.State = StorageConnectionStates.Connected;
    connection.LastError = null;
    connection.UpdatedAt = clock.GetUtcNow().UtcDateTime;
    connection.Revision++;
    try
    {
      await db.SaveChangesAsync(ct);
    }
    catch (Exception ex) when (db.IsWriteConflict(ex))
    {
      return Fail("The connection changed. Reload and try again.", 409);
    }
    return RequestResponse<StorageConnectionView>.Ok(
      StorageConnectionHandlers.View(connection)
    );
  }

  private Task<StorageConnection?> Choosable(
    Guid id,
    CancellationToken ct,
    bool tracked = false
  ) =>
    (tracked ? db.StorageConnections : db.StorageConnections.AsNoTracking())
      .Where(x =>
        x.Id == id
        && x.ProtectedSecret != null
        && (
          x.State == StorageConnectionStates.NeedsRoot
          || x.State == StorageConnectionStates.Connected
        )
      )
      .SingleOrDefaultAsync(ct);

  private static RequestResponse<StorageConnectionView> Fail(
    string message,
    int status
  ) => RequestResponse<StorageConnectionView>.Fail(message, status);
}
