using System.Text.Json;
using Application.Diagnostics;
using Application.Features.Dispatch.Commands.SyncDispatche;
using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Options;
using Application.Features.Dispatch.Queries;
using Application.Features.Fleet.Commands.SyncFleet;
using Application.Features.Fleet.Interfaces;
using Application.Features.Fleet.Queries.GetFleetLocations;
using Application.Features.Fleet.Services;
using Application.Features.Fuel.Services;
using Application.Features.Routing.Background;
using Application.Features.Routing.Commands;
using Application.Features.Routing.Services.FuelPlanning;
using Application.Features.Synchronization.Interfaces;
using Application.Features.Synchronization.Models;
using Application.Features.Synchronization.Options;
using Application.Features.Synchronization.Services;
using Application.Interfaces;
using Domain.Models.Fleet;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Application.Features.Synchronization.Services;

// Publishing what was pulled: one fleet snapshot at a time, so readers see
// either the last whole picture or the next one, never half of each.
public sealed partial class FleetSynchronizationOperation
{
  private async Task PublishAsync(
    IReadOnlyList<VehicleLocationPoint> updates,
    CancellationToken ct,
    bool highFrequency = false,
    TelemetryFeedAccumulator? feed = null,
    string? cursor = null
  )
  {
    using var scope = scopes.CreateScope();
    var services = scope.ServiceProvider;
    var fleet = await services
      .GetRequiredService<FleetCache>()
      .GetAsync(services.GetRequiredService<IAppDbContext>(), ct);
    IReadOnlyList<TruckLocation> stream = highFrequency
      ? await services
        .GetRequiredService<FleetLocationStream>()
        .GetAsync(
          services.GetRequiredService<IFleetTelemetryProvider>(),
          fleet,
          ct
        )
      : [];
    await publishGate.WaitAsync(ct);
    try
    {
      // Do not checkpoint a feed cursor before its snapshot can be published.
      if (feed is not null)
        lock (stateGate)
          state.Apply(feed.Updates, cursor!);
      await PublishSnapshotAsync(updates, fleet, stream);
    }
    finally
    {
      publishGate.Release();
    }
  }

  private async Task PublishSnapshotAsync(
    IReadOnlyList<VehicleLocationPoint> updates,
    IReadOnlyList<FleetTruckInfo> fleet,
    IReadOnlyList<TruckLocation> stream
  )
  {
    // Followers must receive stream positions even if the feed is unavailable.
    lock (stateGate)
      state.ApplyLocations(
        stream.Select(point => new VehicleLocationPoint
        {
          ExternalId = point.TruckExternalId,
          Latitude = point.Latitude,
          Longitude = point.Longitude,
          Speed = point.Speed,
          Heading = point.Heading,
          UpdatedAt = point.UpdatedAt,
          FormattedLocation = point.FormattedLocation,
        })
      );
    List<VehicleTelemetry> vehicles;
    var observedAt = DateTime.UtcNow;
    lock (stateGate)
      vehicles = state
        .Vehicles.Values.Select(x => new VehicleTelemetry
        {
          ExternalId = x.ExternalId,
          Latitude = x.Latitude,
          Longitude = x.Longitude,
          Speed = x.Speed,
          Heading = x.Heading,
          UpdatedAt = x.UpdatedAt,
          ObservedAt = observedAt,
          FormattedLocation = x.FormattedLocation,
          EngineState = x.EngineState,
          FuelPercent = x.FuelPercent,
          FuelUpdatedAt = x.FuelUpdatedAt,
          OutsideTemperatureCelsius = x.OutsideTemperatureCelsius,
          OutsideTemperatureUpdatedAt = x.OutsideTemperatureUpdatedAt,
        })
        .ToList();
    var active = fleet
      .Where(x => x.IsActive)
      .ToDictionary(x => x.TruckExternalId);
    TruckLocation Map(FleetTruckInfo truck, VehicleLocationPoint location) =>
      new()
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
        ObservedAt = observedAt,
        FormattedLocation = location.FormattedLocation,
      };
    var trucks = vehicles
      .Where(x => x.UpdatedAt != default && active.ContainsKey(x.ExternalId))
      .Select(x =>
      {
        var item = Map(
          active[x.ExternalId],
          new()
          {
            Latitude = x.Latitude,
            Longitude = x.Longitude,
            Speed = x.Speed,
            Heading = x.Heading,
            UpdatedAt = x.UpdatedAt,
            FormattedLocation = x.FormattedLocation,
          }
        );
        item.ObservedAt = x.ObservedAt;
        item.EngineState = x.EngineState;
        item.FuelPercent = x.FuelPercent;
        item.FuelUpdatedAt = x.FuelUpdatedAt;
        item.OutsideTemperatureCelsius = x.OutsideTemperatureCelsius;
        item.OutsideTemperatureUpdatedAt = x.OutsideTemperatureUpdatedAt;
        return item;
      })
      .ToList();
    var points = (telemetry.Current?.Points ?? [])
      .Concat(stream)
      .Concat(
        updates
          .Where(x => active.ContainsKey(x.ExternalId))
          .Select(x => Map(active[x.ExternalId], x))
      )
      .Where(x => x.UpdatedAt > DateTime.UtcNow.AddMinutes(-2))
      .DistinctBy(x => (x.TruckId, x.UpdatedAt))
      .OrderBy(x => x.UpdatedAt)
      .ToList();
    FleetLocationSnapshot.UpdateFromStream(
      trucks,
      (telemetry.Current?.Trucks ?? []).Concat(stream)
    );
    telemetry.Set(new() { Trucks = trucks, Points = points });
    await PersistPositionsAsync(trucks);
  }

  // Positions outlive this process, so an instance that does not collect
  // telemetry can still draw the map and a restart does not start blind. The
  // high-frequency points are not kept: they describe a trail, not a place.
  // A failure to persist is not a failed cycle; the snapshot already holds it.
  private async Task PersistPositionsAsync(IReadOnlyList<TruckLocation> trucks)
  {
    try
    {
      await using var scope = scopes.CreateAsyncScope();
      await scope
        .ServiceProvider.GetRequiredService<ITruckLocationStore>()
        .WriteAsync(trucks, CancellationToken.None);
    }
    catch (Exception ex)
    {
      logger.LogWarning(
        ex,
        "Background operation {Operation} could not persist positions.",
        "fleet-telemetry"
      );
    }
  }
}
