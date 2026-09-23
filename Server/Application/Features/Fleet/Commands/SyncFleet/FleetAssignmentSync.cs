using Application.Features.Fleet.Services;
using Domain.Models.Fleet;

namespace Application.Features.Fleet.Commands.SyncFleet;

public static class FleetAssignmentSync
{
  public static async Task<int> SyncAsync(
    IAppDbContext dbContext,
    IReadOnlyList<ExternalFleetAssignment> assignments,
    IReadOnlyList<ExternalTrailerAssignment> trailerAssignments,
    DateTime snapshotTime,
    string source,
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

    var driverTargets = new Dictionary<Guid, Guid>();

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

    foreach (var truck in trucks)
    {
      var driverId = driverTargets.TryGetValue(truck.Id, out var d)
        ? d
        : (Guid?)null;
      if (truck.DriverId != driverId)
      {
        truck.Driver = null;
        truck.DriverId = null;
      }
    }
    var count = await dbContext.SaveChangesAsync(cancellationToken);
    foreach (var truck in trucks)
      truck.DriverId = driverTargets.TryGetValue(truck.Id, out var d)
        ? d
        : null;

    // The provider's trailer word is kept per truck; which trailer each
    // truck has is then resolved with its current work.
    var trailerIds = dbContext
      .Trailers.Local.Where(x =>
        x.ExternalId.Length > 0 && (x.Source is null || x.Source == source)
      )
      .GroupBy(x => x.ExternalId, StringComparer.OrdinalIgnoreCase)
      .ToDictionary(
        x => x.Key,
        x => x.First().Id,
        StringComparer.OrdinalIgnoreCase
      );
    TruckTrailerAssignments.Record(
      trucks,
      TruckTrailerAssignments.Telemetry(
        selected.ToDictionary(
          x => trucksById[x.VehicleExternalId].Id,
          x => x.DriverExternalId
        ),
        trailerAssignments,
        trailerIds,
        snapshotTime
      )
    );
    count += await dbContext.SaveChangesAsync(cancellationToken);
    count += (
      await TruckTrailerAssignments.ResolveAsync(dbContext, cancellationToken)
    ).Count;
    return count;
  }
}
