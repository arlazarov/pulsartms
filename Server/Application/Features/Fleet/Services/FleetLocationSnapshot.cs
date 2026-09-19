using Application.Features.Fleet.Models;

namespace Application.Features.Fleet.Services;

public static class FleetLocationSnapshot
{
  public static void UpdateFromStream(
    IEnumerable<TruckLocation> trucks,
    IEnumerable<TruckLocation> points
  )
  {
    var latest = new Dictionary<Guid, TruckLocation>();
    foreach (var point in points)
      if (
        !latest.TryGetValue(point.TruckId, out var previous)
        || point.UpdatedAt > previous.UpdatedAt
        || point.UpdatedAt == previous.UpdatedAt
          && point.ObservedAt > previous.ObservedAt
      )
        latest[point.TruckId] = point;

    foreach (var truck in trucks)
    {
      if (
        !latest.TryGetValue(truck.TruckId, out var point)
        || point.UpdatedAt == default
        || point.UpdatedAt < truck.UpdatedAt
        || point.UpdatedAt == truck.UpdatedAt
          && point.ObservedAt < truck.ObservedAt
      )
        continue;
      // Address and GPS must belong to one observation; never attach an older
      // ZIP to a newer position.
      truck.Latitude = point.Latitude;
      truck.Longitude = point.Longitude;
      truck.Speed = point.Speed;
      truck.Heading = point.Heading;
      truck.UpdatedAt = point.UpdatedAt;
      truck.ObservedAt = point.ObservedAt;
      truck.FormattedLocation = string.IsNullOrWhiteSpace(
        point.FormattedLocation
      )
        ? string.Empty
        : point.FormattedLocation;
    }
  }
}
