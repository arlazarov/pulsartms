using Application.Interfaces;
using Microsoft.EntityFrameworkCore;

// Deliberately outside Features: this is reference data several modules
// read, not a capability of any one of them. Left under Fleet it added a
// dependency from Execution, Dispatch and Routing onto Fleet for the sake
// of three names, which ModuleDependencyTests refused - correctly.
namespace Application.Reference;

// Names share the company-scoped fleet catalog cache and its invalidation.
// Each request retains one catalog snapshot.
public sealed class FleetNames(IAppDbContext db, IReadCache? reads = null)
{
  public sealed record Catalog(
    Dictionary<Guid, string> Trucks,
    Dictionary<Guid, string> Drivers,
    Dictionary<Guid, string> Trailers
  );

  private Catalog? catalog;

  public async Task<IReadOnlyDictionary<Guid, string>> TrucksAsync(
    CancellationToken ct
  ) => (await ReadAsync(ct)).Trucks;

  public async Task<IReadOnlyDictionary<Guid, string>> DriversAsync(
    CancellationToken ct
  ) => (await ReadAsync(ct)).Drivers;

  public async Task<IReadOnlyDictionary<Guid, string>> TrailersAsync(
    CancellationToken ct
  ) => (await ReadAsync(ct)).Trailers;

  private async Task<Catalog> ReadAsync(CancellationToken ct) =>
    catalog ??= reads is null
      ? await LoadAsync(ct)
      : await reads.GetAsync(
        "fleet-catalog",
        "names",
        () => LoadAsync(ct),
        ct: ct
      );

  private async Task<Catalog> LoadAsync(CancellationToken ct)
  {
    var rows = await db
      .Trucks.AsNoTracking()
      .Select(x => new
      {
        Kind = 0,
        x.Id,
        Name = x.UnitNumber,
      })
      .Concat(
        db.Drivers.AsNoTracking()
          .Select(x => new
          {
            Kind = 1,
            x.Id,
            x.Name,
          })
      )
      .Concat(
        db.Trailers.AsNoTracking()
          .Select(x => new
          {
            Kind = 2,
            x.Id,
            Name = x.UnitNumber,
          })
      )
      .ToListAsync(ct);
    return new(
      rows.Where(x => x.Kind == 0).ToDictionary(x => x.Id, x => x.Name),
      rows.Where(x => x.Kind == 1).ToDictionary(x => x.Id, x => x.Name),
      rows.Where(x => x.Kind == 2).ToDictionary(x => x.Id, x => x.Name)
    );
  }
}
