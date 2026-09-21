using Domain.Entities.Fleet;
using Domain.Models.Fleet;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Application.Features.Fleet.Services;

internal static class FleetConfigurationReader
{
  public static async Task<IFleetConfiguration?> FindAsync(
    IAppDbContext db,
    string kind,
    Guid id,
    CancellationToken ct
  ) =>
    kind switch
    {
      "trucks" => await db.Trucks.SingleOrDefaultAsync(x => x.Id == id, ct),
      "trailers" => await db.Trailers.SingleOrDefaultAsync(x => x.Id == id, ct),
      "drivers" => await db.Drivers.SingleOrDefaultAsync(x => x.Id == id, ct),
      _ => null,
    };

  public static IQueryable<Load> References(
    IAppDbContext db,
    IFleetConfiguration resource
  )
  {
    var id = resource.Id;
    var query = db
      .Dispatches.AsNoTracking()
      .Where(x =>
        x.Status != "completed"
        && x.Status != "delivered"
        && x.Status != "cancelled"
        && x.Status != "canceled"
        && x.Status != "invoiced"
        && x.Status != "paid"
      );
    return resource switch
    {
      Truck => query.Where(x =>
        x.TruckId == id
        || x.PlanningTruckId == id
        || x.Stops.Any(s => s.TruckId == id)
      ),
      Trailer => query.Where(x =>
        x.TrailerId == id || x.Stops.Any(s => s.TrailerId == id)
      ),
      _ => query.Where(x =>
        x.DriverId == id
        || x.Stops.Any(s => s.DriverId == id || s.CoDriverId == id)
      ),
    };
  }

  public static async Task<string[]> AssignmentsAsync(
    IAppDbContext db,
    IFleetConfiguration resource,
    CancellationToken ct
  )
  {
    var id = resource.Id;
    if (resource is Truck)
    {
      var truck = await db
        .Trucks.AsNoTracking()
        .Where(x => x.Id == id)
        .Select(x => new
        {
          Driver = x.Driver != null ? x.Driver.Name : null,
          Trailer = x.Trailer != null ? x.Trailer.UnitNumber : null,
        })
        .SingleAsync(ct);
      return new[]
      {
        truck.Driver is null ? null : $"Driver: {truck.Driver}",
        truck.Trailer is null ? null : $"Trailer: {truck.Trailer}",
      }
        .OfType<string>()
        .ToArray();
    }
    return await db
      .Trucks.AsNoTracking()
      .Where(x => resource is Trailer ? x.TrailerId == id : x.DriverId == id)
      .Select(x => "Truck: " + x.UnitNumber)
      .ToArrayAsync(ct);
  }

  public static Task<bool> HasExecutionAsync(
    IAppDbContext db,
    IFleetConfiguration resource,
    CancellationToken ct
  )
  {
    var id = resource.Id;
    var query = db
      .ExecutionLegs.AsNoTracking()
      .Where(x => x.Status == "active" || x.Status == "planned");
    query = resource switch
    {
      Truck => query.Where(x => x.TruckId == id),
      Trailer => query.Where(x => x.TrailerId == id),
      _ => query.Where(x =>
        x.DriverId == id
        || x.CoDriverId == id
        || x.Stops.Any(s =>
          s.HasDriverOverride && (s.DriverId == id || s.CoDriverId == id)
        )
      ),
    };
    return query.AnyAsync(ct);
  }

  public static async Task<FleetConfigurationState> StateAsync(
    IAppDbContext db,
    IFleetConfiguration resource,
    CancellationToken ct
  )
  {
    var (name, vin, card, importedName, importedVin) = resource switch
    {
      Truck x => (x.UnitNumber, x.Vin, "", x.UnitNumber, x.ImportedVin),
      Trailer x => (x.UnitNumber, x.Vin, "", x.UnitNumber, x.ImportedVin),
      Driver x => (x.Name, "", x.FuelCard, x.ImportedName, (string?)null),
      _ => throw new InvalidOperationException("Unknown fleet resource."),
    };
    return new(
      new(
        resource.Id,
        name,
        vin,
        resource.IsActive,
        resource.IsLocallyConfigured,
        resource.ConfigurationRevision
      ),
      card,
      importedName,
      importedVin,
      resource.ImportedIsActive,
      await AssignmentsAsync(db, resource, ct),
      await References(db, resource)
        .OrderBy(x => x.Id)
        .Select(x => new FleetConfigurationDispatch(x.Id, x.LoadNumber))
        .Take(30)
        .ToArrayAsync(ct),
      resource.ConfiguredAt
    );
  }
}
