using Application.Features.Fleet.Services;
using Application.Features.Synchronization.Options;
using Application.Features.Synchronization.Services;
using Application.Features.Fleet.Interfaces;
using Application.Features.Fleet.Models;
using Application.Models;
using Microsoft.Extensions.Options;

namespace Application.Features.Fleet.Queries.GetFleetLocations;

public record GetFleetLocationsQuery(bool CachedOnly = false, bool IncludePoints = true)
  : IRequest<RequestResponse<FleetLocationsResponse>>;

public class GetFleetLocationsHandler(
  IAppDbContext dbContext,
  IFleetTelemetryProvider telemetryProvider,
  FleetCache fleetCache,
  FleetTelemetryCache telemetryCache,
  FleetLocationStream stream,
  ServerTelemetry serverTelemetry,
  IOptions<SynchronizationOptions> syncOptions
) : IRequestHandler<GetFleetLocationsQuery, RequestResponse<FleetLocationsResponse>>
{
  public async Task<RequestResponse<FleetLocationsResponse>> Handle(
    GetFleetLocationsQuery request,
    CancellationToken cancellationToken
  )
  {
    var snapshot = syncOptions.Value.Enabled ? serverTelemetry.Current ?? new()
      : request.CachedOnly ? telemetryCache.Latest ?? new()
      : await telemetryCache.GetAsync(LoadAsync, cancellationToken);
    return RequestResponse<FleetLocationsResponse>.Ok(request.IncludePoints ? snapshot : WithoutPoints(snapshot));
  }

  // Board views need current positions only; the shared snapshot is neither copied nor mutated.
  private static FleetLocationsResponse WithoutPoints(FleetLocationsResponse snapshot) =>
    snapshot.Points.Count == 0 ? snapshot : new() { Trucks = snapshot.Trucks, Revision = snapshot.Revision };

  private async Task<FleetLocationsResponse> LoadAsync(CancellationToken cancellationToken)
  {
    var fleet = await fleetCache.GetAsync(dbContext, cancellationToken);
    var telemetry = await telemetryProvider.GetVehicleTelemetryAsync(cancellationToken);

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
          }
      )
      .ToList();

    var points = syncOptions.Value.HighFrequencyLocations
      ? await stream.GetAsync(telemetryProvider, fleet, cancellationToken) : [];

    return new FleetLocationsResponse { Trucks = trucks, Points = points };
  }
}
