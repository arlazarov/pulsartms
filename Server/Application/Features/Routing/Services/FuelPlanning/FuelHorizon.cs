using System.Collections.Immutable;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application.Diagnostics;
using Application.Features.Dispatch.Models;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Services.Addresses;
using Application.Features.Routing.Services.Deadheads;
using Application.Features.Routing.Services.Routes;
using Domain.Entities.Dispatch;
using Domain.Models.Routing;
using Domain.Rules;
using Domain.Rules.Routing;

namespace Application.Features.Routing.Services.FuelPlanning;

public sealed class FuelHorizon(
  IFuelWorkInputsReader inputs,
  IAppDbContext db,
  DeadheadService deadheads
)
{
  public async Task<FuelHorizonResult> BuildAsync(
    RoutePlanningState state,
    TruckRouteProfile profile,
    CancellationToken ct,
    FuelWorkInputs? suppliedInputs = null
  )
  {
    ct.ThrowIfCancellationRequested();
    // "build-total" contains every fuel-horizon stage below it. The stages
    // around the loop - "connection" and "base" - are totals over however many
    // times the loop ran, so read them with their Count; and each of those
    // contains the reads recorded inside it, so they are a breakdown and not
    // an addition.
    using var building = PerformanceStages.Start("fuel-horizon", "build-total");
    var plan = state.Plan!;
    var reading = Stopwatch.GetTimestamp();
    var captured =
      suppliedInputs ?? await inputs.ReadFreshAsync(plan.TruckId, ct);
    // Records nothing when the caller supplied the inputs, which the edit path
    // does: a missing row means it was handed them, not that it was free.
    if (suppliedInputs is null)
      PerformanceStages.Elapsed("fuel-horizon", "inputs", reading);
    var loads = captured.Select(plan);
    var index = loads.FindIndex(x =>
      x.Id == plan.DispatchId && x.ExecutionLegId == plan.ExecutionLegId
    );
    if (index < 0)
      throw new RoutePlanningException(
        "Current dispatch assignment changed. Reload the route before finding fuel."
      );
    var current = captured.Resolve(loads[index]);
    if (
      current.ExecutionLegId != plan.ExecutionLegId
      || current.AssignmentRevision != plan.AssignmentRevision
    )
      throw new RoutePlanningException(
        "Execution changed. Reload the route before finding fuel."
      );
    var completed = current
      .Stops.Where(s => s.IsCompleted)
      .Select(s => s.Id)
      .ToHashSet();
    var now = DateTime.UtcNow;
    var currentStops = current.Stops.ToDictionary(stop => stop.Id);
    var stops = plan
      .Stops.Where(s =>
        !plan.Tracking.PassedStopIds.Contains(s.Id) && !completed.Contains(s.Id)
      )
      .Select(stop =>
        currentStops.TryGetValue(stop.Id, out var source)
          ? ConfirmSavedStop(stop, source, now)
          : throw new RoutePlanningException(
            "Current stops changed. Reload the saved route before finding fuel."
          )
      )
      .ToList();
    var currentStopCount = stops.Count;
    if (stops.Count == 0)
      throw new RoutePlanningException("No remaining dispatch stops.");
    var start = state.Progress!.Position!;
    var currentRoad =
      !plan.FromCurrentPosition && plan.Route.Legs.Count + 1 == plan.Stops.Count
        ? new RoutePlan
        {
          FromCurrentPosition = true,
          InputsChanged = plan.InputsChanged,
          Route = plan.Route,
          Stops = plan.Stops.Skip(1).ToList(),
        }
        : plan;
    var route = RemainingFuelRoute.TryRead(
      currentRoad,
      stops,
      start,
      FuelAccessEstimate.CurrentPositionToleranceMiles
    );
    if (route is null)
      throw new RoutePlanningException(
        "A matching saved current route is required before finding fuel. The saved fuel plan has been kept."
      );
    RoutePoint origin = route.Legs[0].Points[0];
    var startAccessMiles = FuelAccessEstimate.DistanceMiles(
      RouteGeometry.Distance(start, origin)
    );
    var anchors = new[] { origin }.Concat(stops.Select(s => s.Point)).ToList();
    FuelHorizonRoad.RequireAnchored(route, anchors);
    var ids = new List<Guid> { plan.DispatchId };
    var owners = stops.Select(_ => loads[index]).ToList();
    var previous = current;
    var notes = new List<string>();
    var history = ImmutableArray.CreateBuilder<DeadheadHistoryBatch>();
    var roads = ImmutableArray.CreateBuilder<SavedRoadVersion>();
    foreach (var next in loads.Skip(index + 1))
    {
      var load = captured.Resolve(next);
      if (
        load.TruckId != plan.TruckId
        || load.ExecutionLegId != next.ExecutionLegId
        || load.AssignmentRevision != next.AssignmentRevision
        || load.Stops.Any(s => s.TruckId.HasValue && s.TruckId != plan.TruckId)
      )
        throw new RoutePlanningException(
          "A future dispatch has a different truck assignment. Review assignments before calculating fuel."
        );
      var future = new List<PlanStop>();
      var orderedStops = load.Stops.OrderBy(s => s.Sequence).ToList();
      foreach (var stop in orderedStops.Where(s => !s.IsCompleted))
      {
        var address = string.Join(
          ", ",
          new[]
          {
            stop.Address,
            stop.City,
            stop.Province,
            stop.ZipCode,
            stop.Country,
          }.Where(s => !string.IsNullOrWhiteSpace(s))
        );
        var point = ConfirmedPoint(stop, now);
        future.Add(
          new(stop.Id, stop.Name, address, stop.Sequence, point)
          {
            Job = stop.Job,
            StateAfter = stop.StateAfter,
            ScheduledDate = stop.ScheduledDate,
            ScheduledTime = stop.ScheduledTime,
            ScheduledDate2 = stop.ScheduledDate2,
            ScheduledTime2 = stop.ScheduledTime2,
            AppointmentTimeZoneId = stop.AppointmentTimeZoneId,
          }
        );
      }
      if (future.Count == 0)
        continue;
      if (stops.Count + future.Count > 40)
        throw new RoutePlanningException(
          "The assigned itinerary exceeds 40 stops. A complete fuel plan cannot be calculated within the route limit."
        );
      var points = orderedStops
        .Select(stop => ConfirmedPoint(stop, now))
        .ToArray();
      var connecting = Stopwatch.GetTimestamp();
      var (connection, dependency, road) = await ReadConnectionAsync(
        previous,
        load,
        profile,
        ct
      );
      PerformanceStages.Elapsed("fuel-horizon", "connection", connecting);
      if (dependency is not null)
        history.Add(dependency);
      roads.Add(road);
      if (connection is null || !RouteAnchoring.Continuous(route, connection))
        throw new RoutePlanningException(
          "A matching saved connection to the next load is required before finding fuel. The saved fuel plan has been kept."
        );
      FuelHorizonRoad.RequireAnchored(
        connection,
        [route.Legs[^1].Points[^1], points[0]]
      );
      var extension = connection;
      if (points.Length > 1)
      {
        var basing = Stopwatch.GetTimestamp();
        var basis = await ReadBaseAsync(load, profile, points, ct);
        PerformanceStages.Elapsed("fuel-horizon", "base", basing);
        roads.AddRange(basis.Roads);
        extension = FuelHorizonRoad.Join(connection, basis.Route);
      }
      if (future.Count != orderedStops.Count)
        extension = FuelHorizonRoad.RemainingExtension(
          extension,
          orderedStops,
          future
        );
      // End at an actual dispatch stop; never compare variants with different
      // endpoints.
      route = FuelHorizonRoad.Join(route, extension);
      stops.AddRange(future);
      owners.AddRange(future.Select(_ => next));
      ids.Add(next.Id);
      previous = load;
    }
    notes.Add(
      plan.ExecutionLegId.HasValue
      && !FuelHorizonLoads.EndsAtDelivery(loads[^1])
        ? "Covers assigned work through the next transfer. Fuel beyond that transfer is not included."
        : $"Covers {ids.Count} assigned dispatch(es), including connecting deadhead. Future stops are provisional recommendations."
    );
    double end = 0;
    var itinerary = stops
      .Select(
        (stop, i) =>
          new FuelItineraryStop(owners[i].Id, stop, end += route.Legs[i].Miles)
          {
            ExecutionLegId = owners[i].ExecutionLegId,
            AssignmentRevision = owners[i].AssignmentRevision,
          }
      )
      .ToList();
    return new(
      route,
      stops,
      currentStopCount,
      ids,
      FuelWorkSignature.Signature(loads),
      notes
    )
    {
      Itinerary = itinerary,
      History = history.ToImmutable(),
      Roads = roads.ToImmutable(),
      StartAccessMiles = startAccessMiles,
      DispatchSignatures = loads
        .Where(x => ids.Contains(x.Id))
        .ToDictionary(x => x.Id, FuelWorkSignature.LoadSignature),
    };
  }

  private async Task<(
    TruckRoute? Route,
    DeadheadHistoryBatch? History,
    SavedRoadVersion Road
  )> ReadConnectionAsync(
    RouteWorkSnapshot previous,
    RouteWorkSnapshot next,
    TruckRouteProfile profile,
    CancellationToken ct
  )
  {
    if (!previous.ExecutionLegId.HasValue)
    {
      // The two branches are recorded apart: which one a load takes depends on
      // whether its predecessor has an execution leg, and they do different
      // work. A missing row means that branch was not taken.
      var capturing = Stopwatch.GetTimestamp();
      var captured = await deadheads.CaptureRouteAsync(
        previous.Id,
        next,
        profile,
        ct
      );
      PerformanceStages.Elapsed("fuel-horizon", "capture-route", capturing);
      return captured;
    }
    // The captured itinerary selected this predecessor. Its execution owns
    // the delivery assignment even when the imported load retains old trucks.
    var history = new Dictionary<Guid, DeadheadHistorySnapshot>
    {
      [next.Id] = DeadheadHistoryProjection.Capture(
        new(next, [previous], false)
      ),
    };
    var rereading = Stopwatch.GetTimestamp();
    var saved = await deadheads.ReadCapturedRouteAsync(
      previous.Id,
      next,
      profile,
      history,
      ct
    );
    PerformanceStages.Elapsed("fuel-horizon", "read-captured-route", rereading);
    return (saved.Route, null, saved.Road);
  }

  internal static RoutePoint ConfirmedPoint(RouteWorkStop stop, DateTime now)
  {
    if (StopLocation.ReliablePoint(stop, now) is { } reliable)
      return reliable;
    throw new RoutePlanningException(
      "A confirmed saved stop location is required before finding fuel. The saved fuel plan has been kept."
    );
  }

  private static PlanStop ConfirmSavedStop(
    PlanStop saved,
    RouteWorkStop current,
    DateTime now
  )
  {
    _ = ConfirmedPoint(current, now);
    return saved;
  }

  private async Task<(
    TruckRoute Route,
    ImmutableArray<SavedRoadVersion> Roads
  )> ReadBaseAsync(
    RouteWorkSnapshot load,
    TruckRouteProfile profile,
    IReadOnlyList<RoutePoint> points,
    CancellationToken ct
  )
  {
    var basing = Stopwatch.GetTimestamp();
    var saved = await db
      .DispatchBaseRoutes.AsNoTracking()
      .SingleOrDefaultAsync(
        row =>
          row.DispatchId == load.Id
          && row.ExecutionLegId == load.ExecutionLegId,
        ct
      );
    PerformanceStages.Elapsed("fuel-horizon", "base-route-read", basing);
    var roads = ImmutableArray.CreateBuilder<SavedRoadVersion>();
    roads.Add(
      SavedRoadVersion.Base(
        NextLoadRouteVersion.From(
          load.Id,
          load.ExecutionLegId,
          new(load.Id, saved, null)
        )
      )
    );
    var route =
      saved?.InputHash == BaseRouteService.Signature(load, profile)
        ? SavedRouteReader.Route(saved.RouteJson, points.Count - 1)
        : null;
    if (route is null)
    {
      var loading = Stopwatch.GetTimestamp();
      var stored = await db
        .DispatchRoutePlans.AsNoTracking()
        .SingleOrDefaultAsync(
          row =>
            row.DispatchId == load.Id
            && row.ExecutionLegId == load.ExecutionLegId,
          ct
        );
      await RoutePlanStorage.LoadAsync(db, stored, ct);
      // The row and its chunked geometry together - one is worthless without
      // the other, and the load is where the geometry actually arrives.
      PerformanceStages.Elapsed("fuel-horizon", "base-plan-read", loading);
      var previous =
        stored?.InputHash == RoutePlanInputs.Hash(load, profile)
        && stored.TruckId == load.TruckId
        && stored.AssignmentRevision == load.AssignmentRevision
          ? RoutePlanStorage.Read(stored)
          : null;
      if (
        previous is { FromCurrentPosition: false }
        && previous.DispatchId == load.Id
        && previous.TruckId == load.TruckId
        && previous.ExecutionLegId == load.ExecutionLegId
        && previous.AssignmentRevision == load.AssignmentRevision
      )
      {
        route = previous.Route;
        roads.Add(SavedRoadVersion.Plan(stored!, previous));
      }
    }
    if (route is null)
      throw new RoutePlanningException(
        "A matching saved base route is required for every assigned load before finding fuel. The saved fuel plan has been kept."
      );
    FuelHorizonRoad.RequireAnchored(route, points);
    return (route, roads.ToImmutable());
  }
}
