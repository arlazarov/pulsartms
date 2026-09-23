using Application.Storage;
using Domain.Entities.Storage;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Infrastructure.Storage;

// PulsR storage in the application database, for development and tests
// only: bytes in a bytea row, a whole file in memory while it is written or
// read. It is used only where configuration names it
// (Storage:Managed:Provider = "database"); production uses object storage.
public sealed class DatabaseFileStorage(
  AppDbContext db,
  IConfiguration configuration
) : IFileStorageProvider
{
  public string Kind => StorageKinds.Managed;

  public bool IsAvailable =>
    string.Equals(
      configuration["Storage:Managed:Provider"],
      "database",
      StringComparison.OrdinalIgnoreCase
    );

  public long MaximumSize => 16 * 1024 * 1024;

  public Task<string> ReserveKeyAsync(
    StorageTarget target,
    Guid fileId,
    CancellationToken ct
  ) => Task.FromResult(fileId.ToString("N"));

  public async Task PutAsync(
    StorageTarget target,
    string key,
    StorageUpload upload,
    CancellationToken ct
  )
  {
    if (!Guid.TryParseExact(key, "N", out var id))
      throw new StorageUnavailableException("The key is not this store's.");
    if (await ExistsAsync(target, key, ct))
      return;
    // The whole content is read, and checked, before anything is written.
    using var copy = new MemoryStream((int)upload.Length);
    await upload.Content.CopyToAsync(copy, ct);
    db.ManagedFileBlobs.Add(
      new ManagedFileBlob
      {
        Id = id,
        CompanyId = target.Company,
        Content = copy.ToArray(),
      }
    );
    await db.SaveChangesAsync(ct);
  }

  public async Task<bool> ExistsAsync(
    StorageTarget target,
    string key,
    CancellationToken ct
  ) =>
    Guid.TryParseExact(key, "N", out var id)
    && await db
      .ManagedFileBlobs.AsNoTracking()
      .AnyAsync(x => x.Id == id && x.CompanyId == target.Company, ct);

  public async Task<Stream?> OpenAsync(
    StorageTarget target,
    string key,
    CancellationToken ct
  ) =>
    Guid.TryParseExact(key, "N", out var id)
    && await db
      .ManagedFileBlobs.AsNoTracking()
      .Where(x => x.Id == id && x.CompanyId == target.Company)
      .Select(x => x.Content)
      .SingleOrDefaultAsync(ct)
      is { } bytes
      ? new MemoryStream(bytes, writable: false)
      : null;

  public async Task DeleteAsync(
    StorageTarget target,
    string key,
    CancellationToken ct
  )
  {
    if (Guid.TryParseExact(key, "N", out var id))
      await db
        .ManagedFileBlobs.Where(x =>
          x.Id == id && x.CompanyId == target.Company
        )
        .ExecuteDeleteAsync(ct);
  }

  public Task<bool> CheckAsync(StorageTarget target, CancellationToken ct) =>
    Task.FromResult(true);
}
