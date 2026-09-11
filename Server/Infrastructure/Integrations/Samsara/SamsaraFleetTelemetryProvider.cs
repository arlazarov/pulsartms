using Application.Features.Fleet.Interfaces;
using Application.Features.Fleet.Models;

namespace Infrastructure.Integrations.Samsara;

public class SamsaraFleetTelemetryProvider(SamsaraApiService samsaraApi) : IFleetTelemetryProvider, IFleetTelemetryFeedProvider
{
  public async Task<TelemetryFeed> GetFeedAsync(string? cursor, CancellationToken ct)
  {
    var feed = await samsaraApi.GetStatsFeedAsync(cursor, ct);
    var pagination = feed.Pagination ?? throw new InvalidOperationException("Samsara feed cursor is missing.");
    if (string.IsNullOrWhiteSpace(pagination.EndCursor)) throw new InvalidOperationException("Samsara feed cursor is empty.");
    var updates = new List<TelemetryUpdate>();
    foreach (var vehicle in feed.Data)
    {
      foreach (var gps in vehicle.Gps)
        updates.Add(new(vehicle.Id, new() { ExternalId = vehicle.Id, Latitude = gps.Latitude, Longitude = gps.Longitude,
          Speed = gps.SpeedMilesPerHour, Heading = gps.HeadingDegrees, UpdatedAt = gps.Time,
          FormattedLocation = gps.ReverseGeo?.FormattedLocation ?? "" }, null, null, null, null));
      var engine = vehicle.EngineStates.MaxBy(x => x.Time);
      var fuel = vehicle.FuelPercents.MaxBy(x => x.Time);
      if (engine is not null || fuel is not null)
        updates.Add(new(vehicle.Id, null, engine?.Value, engine?.Time, fuel?.Value, fuel?.Time));
    }
    return new(updates, pagination.EndCursor, pagination.HasNextPage);
  }

  public async Task<IReadOnlyList<VehicleTelemetry>> GetVehicleTelemetryAsync(
    CancellationToken cancellationToken = default
  )
  {
    var vehicles = await samsaraApi.GetVehicleLocationsAsync(cancellationToken);
    var observedAt = DateTime.UtcNow;

    return
    [
      .. vehicles
        .Where(x => x.Gps is not null)
        .Select(x => new VehicleTelemetry
        {
          ExternalId = x.Id,
          Latitude = x.Gps!.Latitude,
          Longitude = x.Gps.Longitude,
          Speed = x.Gps.SpeedMilesPerHour,
          Heading = x.Gps.HeadingDegrees,
          UpdatedAt = x.Gps.Time,
          ObservedAt = observedAt,
          FormattedLocation = x.Gps.ReverseGeo?.FormattedLocation ?? string.Empty,
          EngineState = x.EngineState?.Value ?? string.Empty,
          EngineUpdatedAt = x.EngineState?.Time,
          FuelPercent = x.FuelPercent?.Value,
          FuelUpdatedAt = x.FuelPercent?.Time,
        }),
    ];
  }

  public async Task<VehicleLocationStream> GetLocationStreamAsync(
    IReadOnlyCollection<string> vehicleIds,
    DateTime startTime,
    DateTime endTime,
    string? cursor = null,
    CancellationToken cancellationToken = default
  )
  {
    var stream = await samsaraApi.GetLocationSpeedStreamAsync(
      vehicleIds,
      startTime,
      endTime,
      cursor,
      cancellationToken
    );

    return new VehicleLocationStream
    {
      Data =
      [
        .. stream
          .Data.Where(x => x.Location is not null)
          .Select(x =>
          {
            var speedMetersPerSecond =
              x.Speed?.GpsSpeedMetersPerSecond ?? x.Speed?.EcuSpeedMetersPerSecond ?? 0;

            return new VehicleLocationPoint
            {
              ExternalId = x.Asset.Id,
              Latitude = x.Location!.Latitude,
              Longitude = x.Location.Longitude,
              Speed = speedMetersPerSecond * 2.236936m,
              Heading = x.Location.HeadingDegrees,
              UpdatedAt = x.HappenedAtTime,
              FormattedLocation = x.Address?.FormattedAddress ?? string.Empty,
            };
          }),
      ],
      EndCursor = stream.Pagination.EndCursor,
      HasNextPage = stream.Pagination.HasNextPage,
    };
  }
}
