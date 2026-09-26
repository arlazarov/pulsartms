using Application.Features.Fleet.Interfaces;
using Domain.Models.Fleet;

namespace Application.Features.Fleet.Services;

// One request's view of a driver's clocks: who the driver is to the HOS
// provider and which trucks the fleet assigns them, and the shared clock
// snapshot, each read once however many handlers in the request ask. A
// conversation's context asks for the clocks and for the duty status of
// the same driver; both used to read the driver and copy the company's
// whole snapshot, and on a cold instance both read the stored clocks.
// Scoped to the request, so nothing outlives the company, caller or
// freshness it was read under.
public sealed class DriverClockReader(
  IAppDbContext db,
  IDriverHosProvider hos,
  IDriverHosStore store
)
{
  public sealed record Driver(string ExternalId, IReadOnlyList<Guid> Trucks);

  private readonly Dictionary<Guid, Task<Driver?>> drivers = [];
  private Task<IReadOnlyDictionary<string, DriverHosClocks>>? clocks;

  // Null for a driver the provider does not know.
  public Task<Driver?> DriverAsync(Guid driverId, CancellationToken ct)
  {
    if (!drivers.TryGetValue(driverId, out var read))
      drivers[driverId] = read = ReadDriverAsync(driverId, ct);
    return read;
  }

  // The shared snapshot, or what the last refresh stored when this
  // instance holds none: never a provider call.
  public Task<IReadOnlyDictionary<string, DriverHosClocks>> ClocksAsync(
    CancellationToken ct
  ) => clocks ??= ReadClocksAsync(ct);

  private async Task<Driver?> ReadDriverAsync(
    Guid driverId,
    CancellationToken ct
  )
  {
    var row = await db
      .Drivers.AsNoTracking()
      .Where(x => x.Id == driverId)
      .Select(x => new
      {
        x.ExternalId,
        Trucks = db
          .Trucks.Where(t => t.DriverId == x.Id)
          .Select(t => t.Id)
          .ToList(),
      })
      .SingleOrDefaultAsync(ct);
    return string.IsNullOrEmpty(row?.ExternalId)
      ? null
      : new(row.ExternalId, row.Trucks);
  }

  private async Task<
    IReadOnlyDictionary<string, DriverHosClocks>
  > ReadClocksAsync(CancellationToken ct)
  {
    var shared = await hos.GetClocksAsync(ct);
    return shared.Count > 0 ? shared : await store.ReadAsync(ct);
  }
}
