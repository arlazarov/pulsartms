using Application.Interfaces;
using Microsoft.EntityFrameworkCore;

// Deliberately outside Features: this is reference data several modules
// read, not a capability of any one of them. Left under Fleet it added a
// dependency from Execution, Dispatch and Routing onto Fleet for the sake
// of three names, which ModuleDependencyTests refused - correctly.
namespace Application.Reference;

// Truck, driver and trailer names, read once for the request rather than
// once for every caller that needs them.
//
// Each read of this database costs about sixty-six milliseconds whatever it
// asks for, and ExecutionLoads.ReadAsync asked for all three every time it
// ran - roughly fifteen times in one board page load, for tables holding
// four, nine and seven rows. That is around two seconds of a page spent
// fetching twenty rows over and over.
//
// Scoped, so the names cannot drift inside one request.
//
// Across requests they come from the read cache, under the group both of
// their writers already invalidate: the fleet settings command and the
// provider sync each drop "fleet-catalog" whenever they change a row, and the
// relay carries that to the other instances. Once per request was still three
// round trips for every request that touched execution - a map left open on
// a truck asked for these twenty rows three times every ten seconds.
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

  private async Task<Catalog> LoadAsync(CancellationToken ct) =>
    new(
      await db
        .Trucks.AsNoTracking()
        .Select(x => new { x.Id, Name = x.UnitNumber })
        .ToDictionaryAsync(x => x.Id, x => x.Name, ct),
      await db
        .Drivers.AsNoTracking()
        .Select(x => new { x.Id, x.Name })
        .ToDictionaryAsync(x => x.Id, x => x.Name, ct),
      await db
        .Trailers.AsNoTracking()
        .Select(x => new { x.Id, Name = x.UnitNumber })
        .ToDictionaryAsync(x => x.Id, x => x.Name, ct)
    );
}
