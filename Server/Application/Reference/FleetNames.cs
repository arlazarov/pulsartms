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
// Scoped, so the names cannot drift inside one request, and reloaded on the
// next one.
public sealed class FleetNames(IAppDbContext db)
{
  private IReadOnlyDictionary<Guid, string>? trucks;
  private IReadOnlyDictionary<Guid, string>? drivers;
  private IReadOnlyDictionary<Guid, string>? trailers;

  public async Task<IReadOnlyDictionary<Guid, string>> TrucksAsync(
    CancellationToken ct
  ) =>
    trucks ??= await db
      .Trucks.AsNoTracking()
      .Select(x => new { x.Id, Name = x.UnitNumber })
      .ToDictionaryAsync(x => x.Id, x => x.Name, ct);

  public async Task<IReadOnlyDictionary<Guid, string>> DriversAsync(
    CancellationToken ct
  ) =>
    drivers ??= await db
      .Drivers.AsNoTracking()
      .Select(x => new { x.Id, x.Name })
      .ToDictionaryAsync(x => x.Id, x => x.Name, ct);

  public async Task<IReadOnlyDictionary<Guid, string>> TrailersAsync(
    CancellationToken ct
  ) =>
    trailers ??= await db
      .Trailers.AsNoTracking()
      .Select(x => new { x.Id, Name = x.UnitNumber })
      .ToDictionaryAsync(x => x.Id, x => x.Name, ct);
}
