using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application.Caching;
using Application.Features.Dispatch.Models;
using Application.Features.Execution.Models;
using Application.Features.Execution.Queries;
using Application.Features.Execution.Services;
using Application.Features.Fleet.Models;
using Application.Features.Fleet.Queries.GetFleetLocations;
using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Models;
using Application.Features.Routing.Options;
using Application.Features.Routing.Services.Addresses;
using Application.Features.Synchronization.Options;
using Domain.Entities.Dispatch;
using Domain.Entities.Fleet;
using Microsoft.Extensions.Options;
using DispatchEntity = global::Domain.Entities.Dispatch.Dispatch;

namespace Application.Features.Routing.Services.Routes;

// Which work a dispatch is, as the truck's assignment stands: the load, the
// leg being driven, and the one truck every stop of it agrees on.
public sealed partial class RoutePlanningService
{
  public async Task<RouteWorkSnapshot> LoadAsync(
    Guid id,
    CancellationToken ct,
    Guid? executionLegId = null,
    Guid? truckId = null
  )
  {
    Task<DispatchSource?> Load() =>
      db
        .Dispatches.AsNoTracking()
        .Include(x => x.Stops)
        .Where(x => x.Id == id)
        .Select(x => new DispatchSource(
          x,
          db.LoadExecutionLegs.Any(link => link.DispatchId == x.Id)
        ))
        .SingleOrDefaultAsync(ct);
    var key = $"{id}:source:{reads.Generation("execution")}";
    var source =
      (await reads.GetAsync("dispatch", key, Load))
      ?? throw new RoutePlanningException("Dispatch not found.");
    return await ResolveAssignmentAsync(
      source.Load,
      ct,
      executionLegId: executionLegId,
      truckId: truckId,
      confirmedLegacy: !source.HasNativeExecution
    );
  }

  internal async Task<RouteWorkSnapshot> ResolveAssignmentAsync(
    DispatchEntity source,
    CancellationToken ct,
    IReadOnlyDictionary<string, Guid>? knownTrucks = null,
    IReadOnlyDictionary<Guid, string>? knownTruckNumbers = null,
    Guid? executionLegId = null,
    Guid? truckId = null,
    bool confirmedLegacy = false
  )
  {
    var load = RouteWorkProjection.Capture(source);
    if (
      !load.ExecutionLegId.HasValue
      && (!confirmedLegacy || executionLegId.HasValue)
    )
    {
      var execution = await reads.GetAsync(
        "execution",
        $"{load.Id}:{executionLegId}:{truckId}",
        () =>
          mediator.Send(
            new GetExecutionItineraryQuery(load.Id, executionLegId, truckId),
            ct
          )
      );
      if (execution is not null)
        load = RouteWorkProjection.Capture(
          source,
          execution.Leg,
          execution.Stops
        );
    }
    var hasExplicitStart =
      load.PlanningFromStopId.HasValue
      || load.Stops.Any(s => s.ManualAction is not null);
    load = RouteWorkProjection.TruckItinerary(load);
    if (load.Stops.Length == 0 && hasExplicitStart)
      throw new RoutePlanningException(
        "The confirmed truck starting stop is no longer available. Review the assignment."
      );
    if (!load.TruckId.HasValue)
    {
      var ids = load
        .Stops.Where(x => x.TruckId.HasValue)
        .Select(x => x.TruckId!.Value)
        .Distinct()
        .ToList();
      if (ids.Count == 1)
        load = load with { TruckId = ids[0] };
      else if (ids.Count == 0 && !string.IsNullOrWhiteSpace(load.TruckNumber))
        load = load with
        {
          TruckId =
            knownTrucks is null
              ? await db
                .Trucks.Where(x => x.UnitNumber == load.TruckNumber)
                .Select(x => (Guid?)x.Id)
                .FirstOrDefaultAsync(ct)
            : knownTrucks.TryGetValue(load.TruckNumber, out var matchedId)
              ? matchedId
            : null,
        };
    }
    if (
      !load.TruckId.HasValue
      || load.Stops.Any(stop =>
        stop.TruckId.HasValue && stop.TruckId != load.TruckId
      )
    )
      throw new RoutePlanningException(
        "No unique truck assignment is available for this dispatch."
      );
    var assignedNumbers = load
      .Stops.Where(s => !string.IsNullOrWhiteSpace(s.TruckNumber))
      .Select(s => s.TruckNumber.Trim())
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .ToArray();
    if (assignedNumbers.Length > 0)
    {
      var number = knownTruckNumbers is null
        ? await db
          .Trucks.Where(t => t.Id == load.TruckId)
          .Select(t => t.UnitNumber)
          .SingleOrDefaultAsync(ct)
        : knownTruckNumbers.GetValueOrDefault(load.TruckId.Value);
      if (
        assignedNumbers.Any(n =>
          !string.Equals(n, number, StringComparison.OrdinalIgnoreCase)
        )
      )
        throw new RoutePlanningException(
          "The stop truck assignments need verification before routing."
        );
    }
    return load;
  }

  public async Task<TruckLocation?> LocationAsync(
    Guid truckId,
    CancellationToken ct,
    bool cachedOnly = false
  )
  {
    var fleet = await mediator.Send(new GetFleetLocationsQuery(cachedOnly), ct);
    return fleet.Response?.Trucks.FirstOrDefault(x => x.TruckId == truckId);
  }

  public Task<TruckRouteProfile> ProfileAsync(
    Guid truckId,
    CancellationToken ct
  ) => profiles.GetAsync(truckId, ct);
}
