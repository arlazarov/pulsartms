using Domain.Entities.Storage;
using Domain.Rules.Storage;
using Microsoft.Extensions.Options;

namespace Application.Storage;

// The owner of stored files: which connection holds each, its checked state
// and its only object. Callers get files by id; they never see a provider,
// a key or a secret, and they never choose a checked state: every upload
// ends quarantined until a server-side check releases it.
//
// An upload is recorded before the provider is called, with its fingerprint
// (name, type, length, SHA-256) and a key reserved at the provider. One
// attempt at a time holds it through a fenced lease; only the holder
// uploads and completes it. The content is checked against the fingerprint
// before its last byte leaves, and storing under the reserved key never
// makes a second object. A lost answer at any step is settled by asking
// whether the reserved key holds an object; nothing is deleted on an
// ambiguous outcome.
public sealed class FileStore(
  IAppDbContext db,
  IEnumerable<IFileStorageProvider> providers,
  IStorageSecrets secrets,
  ICurrentCompany companies,
  IOptions<StorageOptions> options,
  StorageUploadGate gate,
  TimeProvider clock
)
{
  public long MaximumSize(IFileStorageProvider provider) =>
    Math.Min(
      options.Value.MaximumMegabytes * 1024L * 1024L,
      provider.MaximumSize
    );

  public async Task<StoredFile> PutAsync(
    StoredFileRequest request,
    Stream content,
    CancellationToken ct
  )
  {
    var company = Company();
    if (
      request.Sha256 is not { Length: 64 } sha
      || !sha.All(char.IsAsciiHexDigitLower)
    )
      throw new ArgumentException("The content hash is not a SHA-256.");
    var file = await FileAsync(request.FileId, ct);
    if (file is null)
    {
      var connection = request.ConnectionId is { } id
        ? await ConnectionAsync(id, ct)
        : await DefaultAsync(ct);
      var provider = Provider(connection.Kind);
      if (request.Length <= 0 || request.Length > MaximumSize(provider))
        throw new ArgumentException("The file size is not allowed.");
      var now = clock.GetUtcNow().UtcDateTime;
      var folder = string.Join('/', StorageNaming.Folder(request.Folder ?? []));
      // Readable only: two files may still race to one name, which changes
      // nothing about either file's identity.
      var taken = await db
        .StoredFiles.AsNoTracking()
        .Where(x => x.ConnectionId == connection.Id && x.Folder == folder)
        .Select(x => x.Name)
        .Take(1000)
        .ToListAsync(ct);
      file = new StoredFile
      {
        Id = request.FileId,
        CompanyId = company,
        ConnectionId = connection.Id,
        ObjectKey = await provider.ReserveKeyAsync(
          Target(connection),
          request.FileId,
          ct
        ),
        ContentType = request.ContentType,
        Name = StorageNaming.Unique(StorageNaming.Segment(request.Name), taken),
        Folder = folder,
        OriginalName = StorageNaming.Segment(
          request.OriginalName ?? request.Name
        ),
        Size = request.Length,
        Sha256 = sha,
        State = StoredFileStates.Uploading,
        CreatedAt = now,
        UpdatedAt = now,
      };
      db.StoredFiles.Add(file);
      try
      {
        await db.SaveChangesAsync(ct);
      }
      catch (DbUpdateException)
      {
        // Another first attempt recorded this id; its record decides.
        db.Entry(file).State = EntityState.Detached;
        file =
          await FileAsync(request.FileId, ct)
          ?? throw new StorageUnavailableException(
            "The upload was not recorded."
          );
      }
    }
    if (
      file.Sha256 != sha
      || file.Size != request.Length
      || file.ContentType != request.ContentType
    )
      throw new StorageConflictException();
    if (file.State != StoredFileStates.Uploading)
      return file;
    return await UploadAsync(file, content, ct);
  }

  private async Task<StoredFile> UploadAsync(
    StoredFile file,
    Stream content,
    CancellationToken ct
  )
  {
    var token =
      await ClaimAsync(file.Id, ct) ?? throw new StorageBusyException();
    var connection = await ConnectionAsync(file.ConnectionId, ct);
    var provider = Provider(connection.Kind);
    var target = Target(connection);
    await using var reading = new StorageReading(
      content,
      file.Size,
      file.Sha256
    );
    await gate.Slots.WaitAsync(ct);
    try
    {
      if (!await provider.ExistsAsync(target, file.ObjectKey, ct))
        await provider.PutAsync(
          target,
          file.ObjectKey,
          new(
            file.Id,
            file.Name,
            StorageNaming.Folder([file.Folder]),
            file.ContentType,
            file.Size,
            reading
          ),
          ct
        );
    }
    catch (StorageContentMismatchException)
    {
      // The provider never received the last byte, so no object was made;
      // the upload is refused for good.
      await FinishAsync(file.Id, token, StoredFileStates.Rejected, ct);
      throw;
    }
    finally
    {
      gate.Slots.Release();
    }
    await FinishAsync(file.Id, token, StoredFileStates.Quarantined, ct);
    return (await FileAsync(file.Id, ct))!;
  }

  // Takes the upload for this attempt when nobody holds it or the holder's
  // lease ran out. Null when another attempt holds it.
  public async Task<Guid?> ClaimAsync(Guid fileId, CancellationToken ct)
  {
    var token = Guid.NewGuid();
    var now = clock.GetUtcNow().UtcDateTime;
    var until = now.AddMinutes(options.Value.UploadLeaseMinutes);
    return
      await db
        .StoredFiles.Where(x =>
          x.Id == fileId
          && x.State == StoredFileStates.Uploading
          && (x.UploadLeaseUntil == null || x.UploadLeaseUntil <= now)
        )
        .ExecuteUpdateAsync(
          x =>
            x.SetProperty(f => f.UploadToken, token)
              .SetProperty(f => f.UploadLeaseUntil, until)
              .SetProperty(f => f.UpdatedAt, now),
          ct
        ) == 1
      ? token
      : null;
  }

  // Completes an upload only for the attempt that still holds it; a late
  // attempt whose lease was taken over changes nothing.
  public async Task<bool> FinishAsync(
    Guid fileId,
    Guid token,
    string state,
    CancellationToken ct
  ) =>
    await db
      .StoredFiles.Where(x =>
        x.Id == fileId
        && x.State == StoredFileStates.Uploading
        && x.UploadToken == token
      )
      .ExecuteUpdateAsync(
        x =>
          x.SetProperty(f => f.State, state)
            .SetProperty(f => f.UploadToken, (Guid?)null)
            .SetProperty(f => f.UploadLeaseUntil, (DateTime?)null)
            .SetProperty(f => f.UpdatedAt, clock.GetUtcNow().UtcDateTime),
        ct
      ) == 1;

  private Task<StoredFile?> FileAsync(Guid id, CancellationToken ct) =>
    db.StoredFiles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);

  // The file's bytes when it is available, or when a checker asks for a
  // quarantined one. A file whose object has gone is marked missing. The
  // caller disposes the stream.
  public async Task<(StoredFile File, Stream Content)?> OpenAsync(
    Guid fileId,
    bool quarantined,
    CancellationToken ct
  )
  {
    // Read untracked: states change through conditional updates, which a
    // tracked copy in this context would not see.
    var file = await db
      .StoredFiles.AsNoTracking()
      .SingleOrDefaultAsync(x => x.Id == fileId, ct);
    if (
      file is null
      || !(
        file.State == StoredFileStates.Available
        || quarantined && file.State == StoredFileStates.Quarantined
      )
    )
      return null;
    var connection = await db
      .StorageConnections.AsNoTracking()
      .SingleOrDefaultAsync(x => x.Id == file.ConnectionId, ct);
    if (connection is not { State: StorageConnectionStates.Connected })
      return null;
    var content = await Provider(connection.Kind)
      .OpenAsync(Target(connection), file.ObjectKey, ct);
    if (content is null)
    {
      await SetStateAsync(file.Id, file.State, StoredFileStates.Missing, ct);
      return null;
    }
    // Bounded by the recorded length and checked against the recorded
    // hash, whatever the provider hands back.
    return (
      file,
      new StorageReading(content, file.Size, file.Sha256, ownsInner: true)
    );
  }

  public Task<string?> StateAsync(Guid fileId, CancellationToken ct) =>
    db
      .StoredFiles.AsNoTracking()
      .Where(x => x.Id == fileId)
      .Select(x => x.State)
      .SingleOrDefaultAsync(ct);

  public async Task<bool> SetStateAsync(
    Guid fileId,
    string from,
    string to,
    CancellationToken ct
  ) =>
    await db
      .StoredFiles.Where(x => x.Id == fileId && x.State == from)
      .ExecuteUpdateAsync(
        x =>
          x.SetProperty(f => f.State, to)
            .SetProperty(f => f.UpdatedAt, clock.GetUtcNow().UtcDateTime),
        ct
      ) == 1;

  // The connection new files go to: the company's default, or the managed
  // store created on first use when this server has one. A server without a
  // managed store never substitutes another; the company must choose.
  public async Task<StorageConnection> DefaultAsync(CancellationToken ct)
  {
    var connection = await db
      .StorageConnections.AsNoTracking()
      .SingleOrDefaultAsync(
        x => x.IsDefault && x.State == StorageConnectionStates.Connected,
        ct
      );
    if (connection is not null)
      return connection;
    if (
      await db.StorageConnections.AnyAsync(
        x => x.IsDefault || x.Kind == StorageKinds.Managed,
        ct
      )
    )
      throw new StorageUnavailableException(
        "The default storage is not usable."
      );
    if (
      providers.FirstOrDefault(x => x.Kind == StorageKinds.Managed)
      is not { IsAvailable: true }
    )
      throw new StorageUnavailableException(
        "Choose where to store files first."
      );
    var now = clock.GetUtcNow().UtcDateTime;
    connection = new StorageConnection
    {
      Id = Guid.NewGuid(),
      CompanyId = Company(),
      Kind = StorageKinds.Managed,
      DisplayName = StorageKinds.Find(StorageKinds.Managed)!.Name,
      State = StorageConnectionStates.Connected,
      IsDefault = true,
      CreatedAt = now,
      UpdatedAt = now,
      Revision = 1,
    };
    db.StorageConnections.Add(connection);
    await db.SaveChangesAsync(ct);
    return connection;
  }

  public StorageTarget Target(StorageConnection connection) =>
    new(
      connection.CompanyId,
      connection.Id,
      connection.Root,
      connection.ProtectedSecret is null
        ? null
        : secrets.Unprotect(
          connection.CompanyId,
          connection.Id,
          connection.ProtectedSecret
        )
          ?? throw new StorageUnavailableException(
            "The connection secret cannot be read."
          )
    );

  public IFileStorageProvider Provider(string kind) =>
    providers.FirstOrDefault(x => x.Kind == kind && x.IsAvailable)
    ?? throw new StorageUnavailableException(
      "The storage kind is not available."
    );

  private async Task<StorageConnection> ConnectionAsync(
    Guid id,
    CancellationToken ct
  ) =>
    await db
      .StorageConnections.AsNoTracking()
      .SingleOrDefaultAsync(
        x => x.Id == id && x.State == StorageConnectionStates.Connected,
        ct
      )
    ?? throw new StorageUnavailableException("The connection is not usable.");

  private Guid Company() =>
    companies.Id
    ?? throw new InvalidOperationException("Files belong to a company.");
}

// FileId is the caller's stable identity for this upload and Sha256 (lower
// case hex) the content it promises: a retry with the same id and content
// completes or returns the same file, never a second one; the same id with
// other content is refused.
//
// Name and Folder are how the file reads outside PulsR (see StorageLayouts);
// OriginalName is the name it arrived with, kept for reference.
public sealed record StoredFileRequest(
  Guid FileId,
  string Name,
  string ContentType,
  long Length,
  string Sha256,
  IReadOnlyList<string>? Folder = null,
  string? OriginalName = null,
  Guid? ConnectionId = null
);

// Uploads streaming at once in this process, shared by every scope.
public sealed class StorageUploadGate(IOptions<StorageOptions> options)
{
  public SemaphoreSlim Slots { get; } =
    new(
      options.Value.MaximumConcurrentUploads,
      options.Value.MaximumConcurrentUploads
    );
}
