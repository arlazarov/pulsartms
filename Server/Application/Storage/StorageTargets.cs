using Domain.Entities.Storage;
using Domain.Rules.Storage;
using Microsoft.Extensions.Options;

namespace Application.Storage;

// How a company's storage connections are reached: the connection new files
// go to, a connection's provider and the target a provider is given (with
// its secret unprotected only here), and what size a provider accepts.
// Stored files (FileStore), connection management (StorageConnections,
// StorageRoots) and reconciliation all reach storage through this.
public sealed class StorageTargets(
  IAppDbContext db,
  IEnumerable<IFileStorageProvider> providers,
  IStorageSecrets secrets,
  ICurrentCompany companies,
  IOptions<StorageOptions> options,
  TimeProvider clock
)
{
  public long MaximumSize(IFileStorageProvider provider) =>
    Math.Min(
      options.Value.MaximumMegabytes * 1024L * 1024L,
      provider.MaximumSize
    );

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

  // A connection that is still connected, by id.
  public async Task<StorageConnection> ConnectionAsync(
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
