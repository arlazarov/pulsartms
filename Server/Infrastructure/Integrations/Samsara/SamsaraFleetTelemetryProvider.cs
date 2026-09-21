using Application.Features.Fleet.Interfaces;
using Domain.Models.Fleet;
using Infrastructure.Integrations.Samsara.Models;

namespace Infrastructure.Integrations.Samsara;

public class SamsaraFleetTelemetryProvider(SamsaraApiService samsaraApi)
  : IFleetTelemetryProvider,
    IFleetTelemetryFeedProvider
{
  public async Task<TelemetryFeed> GetFeedAsync(
    string? cursor,
    CancellationToken ct
  )
  {
    var feed = await samsaraApi.GetStatsFeedAsync(cursor, ct);
    var pagination =
      feed.Pagination
      ?? throw new InvalidOperationException("Samsara feed cursor is missing.");
    if (string.IsNullOrWhiteSpace(pagination.EndCursor))
      throw new InvalidOperationException("Samsara feed cursor is empty.");
    var updates = new List<TelemetryUpdate>();
    foreach (var vehicle in feed.Data)
    {
      foreach (var gps in vehicle.Gps)
        updates.Add(
          new(
            vehicle.Id,
            new()
            {
              ExternalId = vehicle.Id,
              Latitude = gps.Latitude,
              Longitude = gps.Longitude,
              Speed = gps.SpeedMilesPerHour,
              Heading = gps.HeadingDegrees,
              UpdatedAt = gps.Time,
              FormattedLocation = gps.ReverseGeo?.FormattedLocation ?? "",
            },
            null,
            null,
            null,
            null
          )
        );
      var engine = vehicle.EngineStates.MaxBy(x => x.Time);
      var fuel = vehicle.FuelPercents.MaxBy(x => x.Time);
      if (engine is not null || fuel is not null)
        updates.Add(
          new(
            vehicle.Id,
            null,
            engine?.Value,
            engine?.Time,
            fuel?.Value,
            fuel?.Time
          )
        );
      var outside = vehicle
        .Gps.Select(x => x.AmbientAirTemperatureMilliC)
        .Concat(vehicle.EngineStates.Select(x => x.AmbientAirTemperatureMilliC))
        .Concat(vehicle.FuelPercents.Select(x => x.AmbientAirTemperatureMilliC))
        .Where(IsTemperature)
        .MaxBy(x => x!.Time);
      if (outside is not null)
        updates.Add(
          new(
            vehicle.Id,
            null,
            null,
            null,
            null,
            null,
            outside.Value / 1000m,
            outside.Time
          )
        );
    }
    return new(updates, pagination.EndCursor, pagination.HasNextPage);
  }

  public async Task<IReadOnlyList<VehicleTelemetry>> GetVehicleTelemetryAsync(
    CancellationToken cancellationToken = default
  )
  {
    var vehiclesTask = samsaraApi.GetVehicleLocationsAsync(cancellationToken);
    var outsideTask = ReadOutsideTemperaturesAsync(cancellationToken);
    await Task.WhenAll(vehiclesTask, outsideTask);
    var vehicles = await vehiclesTask;
    var observedAt = DateTime.UtcNow;
    var outside = await outsideTask;

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
          FormattedLocation =
            x.Gps.ReverseGeo?.FormattedLocation ?? string.Empty,
          EngineState = x.EngineState?.Value ?? string.Empty,
          EngineUpdatedAt = x.EngineState?.Time,
          FuelPercent = x.FuelPercent?.Value,
          FuelUpdatedAt = x.FuelPercent?.Time,
          OutsideTemperatureCelsius =
            outside.GetValueOrDefault(x.Id)?.Value / 1000m,
          OutsideTemperatureUpdatedAt = outside.GetValueOrDefault(x.Id)?.Time,
        }),
    ];
  }

  private static bool IsTemperature(SamsaraTemperature? reading) =>
    reading?.Value is not null && reading.Time != default;

  private async Task<
    Dictionary<string, SamsaraTemperature>
  > ReadOutsideTemperaturesAsync(CancellationToken ct)
  {
    // A missing optional sensor must not block GPS, fuel or engine telemetry.
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
    timeout.CancelAfter(TimeSpan.FromSeconds(3));
    try
    {
      var values = await samsaraApi.GetOutsideTemperaturesAsync(timeout.Token);
      return values
        .Where(x => IsTemperature(x.AmbientAirTemperatureMilliC))
        .GroupBy(x => x.Id)
        .ToDictionary(
          x => x.Key,
          x =>
            x.MaxBy(v =>
              v.AmbientAirTemperatureMilliC!.Time
            )!.AmbientAirTemperatureMilliC!
        );
    }
    catch (HttpRequestException)
    {
      ct.ThrowIfCancellationRequested();
      return [];
    }
    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
    {
      return [];
    }
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
              x.Speed?.GpsSpeedMetersPerSecond
              ?? x.Speed?.EcuSpeedMetersPerSecond
              ?? 0;
            var address = SamsaraLocationAddress.Format(x.Location!.Address);
            if (address.Length == 0)
              address = SamsaraLocationAddress.Format(x.Address);

            return new VehicleLocationPoint
            {
              ExternalId = x.Asset.Id,
              Latitude = x.Location!.Latitude,
              Longitude = x.Location.Longitude,
              Speed = speedMetersPerSecond * 2.236936m,
              Heading = x.Location.HeadingDegrees,
              UpdatedAt = x.HappenedAtTime,
              FormattedLocation = address,
            };
          }),
      ],
      EndCursor = stream.Pagination.EndCursor,
      HasNextPage = stream.Pagination.HasNextPage,
    };
  }
}
