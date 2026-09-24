using Domain.Entities.Storage;
using Domain.Rules.Storage;

namespace Application.Storage;

// Where a file goes and how its connection is reached: the company's
// default connection, its provider and the target a provider is given.
public sealed partial class FileStore
{
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
