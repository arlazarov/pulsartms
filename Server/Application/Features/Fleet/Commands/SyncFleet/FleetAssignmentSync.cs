using Domain.Models.Fleet;

namespace Application.Features.Fleet.Commands.SyncFleet;

public static class FleetAssignmentSync
{
  public static async Task<int> SyncAsync(
    IAppDbContext dbContext,
    IReadOnlyList<ExternalFleetAssignment> assignments,
    IReadOnlyList<ExternalTrailerAssignment> trailerAssignments,
    DateTime snapshotTime,
    CancellationToken cancellationToken = default
  )
  {
    await dbContext.Trucks.LoadAsync(cancellationToken);
    await dbContext.Drivers.LoadAsync(cancellationToken);
    await dbContext.Trailers.LoadAsync(cancellationToken);

    var trucks = dbContext.Trucks.Local.ToList();
    var trucksById = trucks
      .Where(x => x.IsActive && !string.IsNullOrWhiteSpace(x.ExternalId))
      .ToDictionary(x => x.ExternalId, StringComparer.OrdinalIgnoreCase);
    var driversById = dbContext
      .Drivers.Local.Where(x =>
        x.IsActive && !string.IsNullOrWhiteSpace(x.ExternalId)
      )
      .ToDictionary(x => x.ExternalId, StringComparer.OrdinalIgnoreCase);
    var trailersById = dbContext
      .Trailers.Local.Where(x =>
        x.IsActive && !string.IsNullOrWhiteSpace(x.ExternalId)
      )
      .ToDictionary(x => x.ExternalId, StringComparer.OrdinalIgnoreCase);

    var driverTargets = new Dictionary<Guid, Guid>();
    var trailerTargets = new Dictionary<Guid, Guid>();

    var current = assignments
      .Where(x =>
        !x.IsPassenger
        && x.StartTime <= snapshotTime
        && (x.EndTime is null || x.EndTime > snapshotTime)
        && !string.IsNullOrWhiteSpace(x.DriverExternalId)
        && !string.IsNullOrWhiteSpace(x.VehicleExternalId)
      )
      .GroupBy(x => x.DriverExternalId, StringComparer.OrdinalIgnoreCase)
      .SelectMany(group =>
      {
        var latest = group
          .Where(x => x.StartTime == group.Max(y => y.StartTime))
          .ToList();
        return
          latest
            .Select(x => x.VehicleExternalId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count() == 1
          ? latest.Take(1)
          : [];
      })
      .GroupBy(x => x.VehicleExternalId, StringComparer.OrdinalIgnoreCase);

    var selected = new List<ExternalFleetAssignment>();
    foreach (var group in current)
    {
      var latest = group
        .Where(x => x.StartTime == group.Max(y => y.StartTime))
        .ToList();
      if (latest.Count != 1)
        continue;
      var assignment = latest[0];
      if (
        !trucksById.TryGetValue(assignment.VehicleExternalId, out var truck)
        || !driversById.TryGetValue(assignment.DriverExternalId, out var driver)
      )
        continue;
      driverTargets[truck.Id] = driver.Id;
      selected.Add(assignment);
    }

    var trailersByDriver = trailerAssignments
      .Where(x =>
        x.StartTime <= snapshotTime
        && (x.EndTime is null || x.EndTime > snapshotTime)
        && !string.IsNullOrWhiteSpace(x.DriverExternalId)
        && !string.IsNullOrWhiteSpace(x.TrailerExternalId)
      )
      .GroupBy(x => x.DriverExternalId, StringComparer.OrdinalIgnoreCase)
      .ToDictionary(
        x => x.Key,
        x =>
          x.Select(y => y.TrailerExternalId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList(),
        StringComparer.OrdinalIgnoreCase
      );

    var candidates = selected
      .Where(x =>
        trailersByDriver.TryGetValue(x.DriverExternalId, out var ids)
        && ids.Count == 1
      )
      .Select(x => new
      {
        Truck = trucksById[x.VehicleExternalId],
        TrailerId = trailersByDriver[x.DriverExternalId][0],
      })
      .GroupBy(x => x.TrailerId, StringComparer.OrdinalIgnoreCase);

    foreach (var group in candidates)
    {
      if (
        group.Count() != 1
        || !trailersById.TryGetValue(group.Key, out var trailer)
      )
        continue;
      var truck = group.Single().Truck;
      trailerTargets[truck.Id] = trailer.Id;
    }
    foreach (var truck in trucks)
    {
      var driverId = driverTargets.TryGetValue(truck.Id, out var d)
        ? d
        : (Guid?)null;
      var trailerId = trailerTargets.TryGetValue(truck.Id, out var t)
        ? t
        : (Guid?)null;
      if (truck.DriverId != driverId)
      {
        truck.Driver = null;
        truck.DriverId = null;
      }
      if (truck.TrailerId != trailerId)
      {
        truck.Trailer = null;
        truck.TrailerId = null;
      }
    }
    var count = await dbContext.SaveChangesAsync(cancellationToken);
    foreach (var truck in trucks)
    {
      truck.DriverId = driverTargets.TryGetValue(truck.Id, out var d)
        ? d
        : null;
      truck.TrailerId = trailerTargets.TryGetValue(truck.Id, out var t)
        ? t
        : null;
    }
    return count;
  }
}
