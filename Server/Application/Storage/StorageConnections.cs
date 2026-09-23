using Application.Models;
using Domain.Entities.Storage;

namespace Application.Storage;

public sealed record GetStorageSettingsQuery
  : IRequest<RequestResponse<StorageSettings>>;

public sealed record SetDefaultStorageCommand(Guid Id, long ExpectedRevision)
  : IRequest<RequestResponse<StorageConnectionView>>;

public sealed record DisconnectStorageCommand(Guid Id, long ExpectedRevision)
  : IRequest<RequestResponse<StorageConnectionView>>;

public sealed record CheckStorageCommand(Guid Id)
  : IRequest<RequestResponse<StorageConnectionView>>;

public sealed record StorageKindView(
  string Kind,
  string Name,
  bool Implemented,
  bool Available,
  string? Unavailable
);

public sealed record StorageConnectionView(
  Guid Id,
  string Kind,
  string DisplayName,
  string State,
  bool IsDefault,
  string? RootName,
  string? LastError,
  long Revision,
  DateTime CreatedAt
);

public sealed record StorageSettings(
  IReadOnlyList<StorageKindView> Kinds,
  IReadOnlyList<StorageConnectionView> Connections
);

// Which storage a company keeps its files in. Only connected connections can
// be the default; the default cannot be disconnected until another is chosen,
// so new files always have a place to go. Disconnecting keeps the files'
// rows: files in that connection show as unavailable, never as deleted.
public sealed class StorageConnectionHandlers(
  IAppDbContext db,
  FileStore files,
  IEnumerable<IFileStorageProvider> providers,
  IEnumerable<IStorageAuthorization> authorizations,
  IEnumerable<IStorageRootPicker> pickers,
  TimeProvider clock
)
  : IRequestHandler<GetStorageSettingsQuery, RequestResponse<StorageSettings>>,
    IRequestHandler<
      SetDefaultStorageCommand,
      RequestResponse<StorageConnectionView>
    >,
    IRequestHandler<
      DisconnectStorageCommand,
      RequestResponse<StorageConnectionView>
    >,
    IRequestHandler<CheckStorageCommand, RequestResponse<StorageConnectionView>>
{
  public async Task<RequestResponse<StorageSettings>> Handle(
    GetStorageSettingsQuery request,
    CancellationToken ct
  )
  {
    var connections = await db
      .StorageConnections.AsNoTracking()
      .Where(x => x.State != StorageConnectionStates.Pending)
      .OrderByDescending(x => x.IsDefault)
      .ThenBy(x => x.CreatedAt)
      .Take(50)
      .ToListAsync(ct);
    return RequestResponse<StorageSettings>.Ok(
      new(
        [
          .. StorageKinds.All.Select(kind =>
          {
            var unavailable =
              !kind.Implemented ? "Not available yet."
              : !providers.Any(x => x.Kind == kind.Kind && x.IsAvailable)
                ? kind.NeedsConsent
                    ? "The server has no client registration for this provider."
                  : "PulsR storage is not set up on this server."
              : kind.NeedsConsent
              && (
                authorizations.FirstOrDefault(x => x.Kind == kind.Kind)
                  is not { IsConfigured: true }
                || pickers.FirstOrDefault(x => x.Kind == kind.Kind)
                  is not { IsConfigured: true }
              )
                ? "The server has no client registration for this provider."
              : null;
            return new StorageKindView(
              kind.Kind,
              kind.Name,
              kind.Implemented,
              unavailable is null,
              unavailable
            );
          }),
        ],
        [.. connections.Select(View)]
      )
    );
  }

  public async Task<RequestResponse<StorageConnectionView>> Handle(
    SetDefaultStorageCommand request,
    CancellationToken ct
  )
  {
    var connection = await db.StorageConnections.SingleOrDefaultAsync(
      x => x.Id == request.Id,
      ct
    );
    if (connection is null)
      return Fail("Storage connection not found.", 404);
    if (connection.Revision != request.ExpectedRevision)
      return Fail("The connection changed. Reload and try again.", 409);
    if (connection.State != StorageConnectionStates.Connected)
      return Fail("Only a connected storage can be the default.", 409);
    // One default per company is a database rule, so the old one is
    // cleared before the new one is set, in one transaction.
    var now = clock.GetUtcNow().UtcDateTime;
    await using var transaction = await db.Database.BeginTransactionAsync(ct);
    await db
      .StorageConnections.Where(x => x.IsDefault && x.Id != connection.Id)
      .ExecuteUpdateAsync(
        x =>
          x.SetProperty(c => c.IsDefault, false)
            .SetProperty(c => c.UpdatedAt, now)
            .SetProperty(c => c.Revision, c => c.Revision + 1),
        ct
      );
    connection.IsDefault = true;
    var saved = await SaveAsync(connection, now, ct);
    if (saved.Success)
      await transaction.CommitAsync(ct);
    return saved;
  }

  public async Task<RequestResponse<StorageConnectionView>> Handle(
    DisconnectStorageCommand request,
    CancellationToken ct
  )
  {
    var connection = await db.StorageConnections.SingleOrDefaultAsync(
      x => x.Id == request.Id,
      ct
    );
    if (connection is null)
      return Fail("Storage connection not found.", 404);
    if (connection.Revision != request.ExpectedRevision)
      return Fail("The connection changed. Reload and try again.", 409);
    if (connection.IsDefault)
      return Fail("Choose another default storage first.", 409);
    if (connection.Kind == StorageKinds.Managed)
      return Fail("PulsR storage cannot be disconnected.", 409);
    // Files there, or on their way there, would silently become unreadable.
    if (
      await db.StoredFiles.AnyAsync(
        x =>
          x.ConnectionId == connection.Id
          && x.State != StoredFileStates.Failed
          && x.State != StoredFileStates.Rejected,
        ct
      )
    )
      return Fail(
        "Files are stored there. They must be moved before it is disconnected.",
        409
      );
    connection.State = StorageConnectionStates.Disconnected;
    connection.ProtectedSecret = null;
    connection.LastError = null;
    return await SaveAsync(connection, clock.GetUtcNow().UtcDateTime, ct);
  }

  public async Task<RequestResponse<StorageConnectionView>> Handle(
    CheckStorageCommand request,
    CancellationToken ct
  )
  {
    var connection = await db.StorageConnections.SingleOrDefaultAsync(
      x => x.Id == request.Id,
      ct
    );
    if (connection is null)
      return Fail("Storage connection not found.", 404);
    if (
      connection.State
      is not (
        StorageConnectionStates.Connected
        or StorageConnectionStates.Failed
      )
    )
      return Fail("This connection cannot be checked.", 409);
    bool working;
    try
    {
      working = await files
        .Provider(connection.Kind)
        .CheckAsync(files.Target(connection), ct);
    }
    catch (StorageUnavailableException)
    {
      working = false;
    }
    connection.State = working
      ? StorageConnectionStates.Connected
      : StorageConnectionStates.Failed;
    connection.LastError = working ? null : "The provider refused access.";
    return await SaveAsync(connection, clock.GetUtcNow().UtcDateTime, ct);
  }

  public static StorageConnectionView View(StorageConnection x) =>
    new(
      x.Id,
      x.Kind,
      x.DisplayName,
      x.State,
      x.IsDefault,
      x.RootName,
      x.LastError,
      x.Revision,
      x.CreatedAt
    );

  private async Task<RequestResponse<StorageConnectionView>> SaveAsync(
    StorageConnection connection,
    DateTime now,
    CancellationToken ct
  )
  {
    connection.UpdatedAt = now;
    connection.Revision++;
    try
    {
      await db.SaveChangesAsync(ct);
    }
    catch (Exception ex) when (db.IsWriteConflict(ex))
    {
      return Fail("The connection changed. Reload and try again.", 409);
    }
    return RequestResponse<StorageConnectionView>.Ok(View(connection));
  }

  private static RequestResponse<StorageConnectionView> Fail(
    string message,
    int status
  ) => RequestResponse<StorageConnectionView>.Fail(message, status);
}
