using Application.Features.Fleet.Interfaces;
using Application.Features.Fleet.Models;
using Application.Features.Fleet.Services;
using Application.Features.Synchronization.Options;
using Application.Models;
using Microsoft.Extensions.Options;

namespace Application.Features.Fleet.Queries.GetFleetLocations;

public record GetFleetLocationsQuery(bool CachedOnly = false)
  : IRequest<RequestResponse<FleetLocationsResponse>>;

public class GetFleetLocationsHandler(
  IAppDbContext dbContext,
  IFleetTelemetryProvider telemetryProvider,
  FleetCache fleetCache,
  FleetTelemetryCache telemetryCache,
  FleetLocationStream stream,
  ServerTelemetry serverTelemetry,
  ITruckLocationStore positions,
  IOptions<SynchronizationOptions> syncOptions
)
  : IRequestHandler<
    GetFleetLocationsQuery,
    RequestResponse<FleetLocationsResponse>
  >
{
  public async Task<RequestResponse<FleetLocationsResponse>> Handle(
    GetFleetLocationsQuery request,
    CancellationToken cancellationToken
  )
  {
    // An instance that does not collect telemetry, or one that has just
    // restarted, holds no snapshot and draws the last recorded positions
    // instead of an empty map. The trail points are not recorded.
    if (syncOptions.Value.Enabled)
      return RequestResponse<FleetLocationsResponse>.Ok(
        serverTelemetry.Current
          ?? new() { Trucks = await positions.ReadAsync(cancellationToken) }
      );
    if (request.CachedOnly)
      return RequestResponse<FleetLocationsResponse>.Ok(
        telemetryCache.Latest
          ?? new() { Trucks = await positions.ReadAsync(cancellationToken) }
      );
    var response = await telemetryCache.GetAsync(LoadAsync, cancellationToken);
    return RequestResponse<FleetLocationsResponse>.Ok(response);
  }

  private async Task<FleetLocationsResponse> LoadAsync(
    CancellationToken cancellationToken
  )
  {
    var fleet = await fleetCache.GetAsync(dbContext, cancellationToken);
    var telemetry = await telemetryProvider.GetVehicleTelemetryAsync(
      cancellationToken
    );

    var trucks = fleet
      .Where(x => x.IsActive)
      .Join(
        telemetry,
        x => x.TruckExternalId,
        x => x.ExternalId,
        (truck, location) =>
          new TruckLocation
          {
            TruckId = truck.TruckId,
            TruckExternalId = truck.TruckExternalId,
            UnitNumber = truck.UnitNumber,
            DriverName = truck.DriverName,
            TrailerNumber = truck.TrailerNumber,
            Latitude = location.Latitude,
            Longitude = location.Longitude,
            Speed = location.Speed,
            Heading = location.Heading,
            UpdatedAt = location.UpdatedAt,
            ObservedAt = location.ObservedAt,
            FormattedLocation = location.FormattedLocation,
            EngineState = location.EngineState,
            FuelPercent = location.FuelPercent,
            FuelUpdatedAt = location.FuelUpdatedAt,
            OutsideTemperatureCelsius = location.OutsideTemperatureCelsius,
            OutsideTemperatureUpdatedAt = location.OutsideTemperatureUpdatedAt,
          }
      )
      .ToList();

    var points = syncOptions.Value.HighFrequencyLocations
      ? await stream.GetAsync(telemetryProvider, fleet, cancellationToken)
      : [];
    FleetLocationSnapshot.UpdateFromStream(trucks, points);

    return new FleetLocationsResponse { Trucks = trucks, Points = points };
  }
}
