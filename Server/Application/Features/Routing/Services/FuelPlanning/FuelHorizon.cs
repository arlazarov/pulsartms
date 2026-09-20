using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application.Features.Dispatch.Models;
using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services.Addresses;
using Application.Features.Routing.Services.Deadheads;
using Application.Features.Routing.Services.Routes;
using Domain.Entities.Dispatch;
using Domain.Entities.Execution;

namespace Application.Features.Routing.Services.FuelPlanning;

public sealed record FuelHorizonResult(
  TruckRoute Route,
  List<PlanStop> Stops,
  int CurrentStopCount,
  List<Guid> DispatchIds,
  string AssignmentSignature,
  List<string> Notes
)
{
  public List<FuelItineraryStop> Itinerary { get; init; } = [];
  public Dictionary<Guid, string> DispatchSignatures { get; init; } = [];
  public double StartAccessMiles { get; init; }
  public ImmutableArray<DeadheadHistoryBatch> History { get; init; } = [];
  public ImmutableArray<SavedRoadVersion> Roads { get; init; } = [];
}

public sealed class FuelHorizon(
  IFuelWorkInputsReader inputs,
  IAppDbContext db,
  DeadheadService deadheads
)
{
  private const int MaximumGeometryPoints = 200_000;

  public static string LoadSignature(IWorkFacts load) =>
    load.RouteChoiceRevision == 0
      ? StopSignature(load)
      : Convert.ToHexString(
        SHA256.HashData(
          Encoding.UTF8.GetBytes(
            $"{StopSignature(load)}:{load.RouteChoiceRevision}"
          )
        )
      );

  private static string StopSignature(IWorkFacts load)
  {
    var original = LegacyStopSignature(load);
    return load.ExecutionLegId is { } legId
      ? Convert.ToHexString(
        SHA256.HashData(
          Encoding.UTF8.GetBytes(
            $"{original}:{legId}:{load.AssignmentRevision}"
          )
        )
      )
      : original;
  }

  private static string LegacyStopSignature(IWorkFacts load) =>
    StopCompletionIdentity.Revise(
      Convert.ToHexString(
        SHA256.HashData(
          JsonSerializer.SerializeToUtf8Bytes(
            new
            {
              load.Id,
              load.TruckId,
              Stops = load
                .Stops.Where(s => !s.DriverOnly)
                .OrderBy(s => s.Sequence)
                .Select(s => new
                {
                  s.Id,
                  s.Sequence,
                  s.TruckId,
                  s.Job,
                  s.StateAfter,
                  s.OperationRevision,
                  s.Address,
                  s.City,
                  s.Province,
                  s.ZipCode,
                  s.Country,
                  s.Latitude,
                  s.Longitude,
                  s.ScheduledDate,
                  s.ScheduledTime,
                  s.ScheduledDate2,
                  s.ScheduledTime2,
                  s.AppointmentTimeZoneId,
                }),
            },
            RoutePlanningService.Json
          )
        )
      ),
      load.Stops.Where(s => !s.DriverOnly)
        .Select(s => (s.Id, s.ManualCompletionRevision))
    );

  public static string Signature(IEnumerable<IWorkFacts> loads)
  {
    var ordered = loads.ToList();
    if (ordered.Any(x => x.ExecutionLegId.HasValue))
      return Convert.ToHexString(
        SHA256.HashData(
          JsonSerializer.SerializeToUtf8Bytes(
            ordered.Select(x => new
            {
              x.Id,
              x.ExecutionLegId,
              x.AssignmentRevision,
              x.Status,
              Signature = LoadSignature(x),
            }),
            RoutePlanningService.Json
          )
        )
      );
    var original = ItinerarySignature(ordered);
    return ordered.All(load => load.RouteChoiceRevision == 0)
      ? original
      : Convert.ToHexString(
        SHA256.HashData(
          JsonSerializer.SerializeToUtf8Bytes(
            new
            {
              original,
              Choices = ordered.Select(load => new
              {
                load.Id,
                load.RouteChoiceRevision,
              }),
            },
            RoutePlanningService.Json
          )
        )
      );
  }

  private static string ItinerarySignature(IEnumerable<IWorkFacts> loads) =>
    StopCompletionIdentity.Revise(
      Convert.ToHexString(
        SHA256.HashData(
          Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(
              loads.Select(x => new
              {
                x.Id,
                x.TruckId,
                x.Status,
                Stops = x
                  .Stops.Where(s => !s.DriverOnly)
                  .OrderBy(s => s.Sequence)
                  .Select(s => new
                  {
                    s.Sequence,
                    s.TruckId,
                    s.Job,
                    s.StateAfter,
                    s.OperationRevision,
                    s.Address,
                    s.City,
                    s.Province,
                    s.ZipCode,
                    s.Country,
                    s.Latitude,
                    s.Longitude,
                    s.ScheduledDate,
                    s.ScheduledTime,
                    s.ScheduledDate2,
                    s.ScheduledTime2,
                    s.AppointmentTimeZoneId,
                  }),
              }),
              RoutePlanningService.Json
            )
          )
        )
      ),
      loads
        .SelectMany(l => l.Stops)
        .Where(s => !s.DriverOnly)
        .Select(s => (s.Id, s.ManualCompletionRevision))
    );

  public async Task<FuelHorizonResult> BuildAsync(
    RoutePlanningState state,
    TruckRouteProfile profile,
    CancellationToken ct,
    FuelWorkInputs? suppliedInputs = null
  )
  {
    ct.ThrowIfCancellationRequested();
    var plan = state.Plan!;
    var captured =
      suppliedInputs ?? await inputs.ReadFreshAsync(plan.TruckId, ct);
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
    RequireAnchored(route, anchors);
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
      var (connection, dependency, road) = await ReadConnectionAsync(
        previous,
        load,
        profile,
        ct
      );
      if (dependency is not null)
        history.Add(dependency);
      roads.Add(road);
      if (connection is null || !RouteAnchoring.Continuous(route, connection))
        throw new RoutePlanningException(
          "A matching saved connection to the next load is required before finding fuel. The saved fuel plan has been kept."
        );
      RequireAnchored(connection, [route.Legs[^1].Points[^1], points[0]]);
      var extension = connection;
      if (points.Length > 1)
      {
        var basis = await ReadBaseAsync(load, profile, points, ct);
        roads.AddRange(basis.Roads);
        extension = Join(connection, basis.Route);
      }
      if (future.Count != orderedStops.Count)
        extension = RemainingExtension(extension, orderedStops, future);
      // End at an actual dispatch stop; never compare variants with different
      // endpoints.
      route = Join(route, extension);
      stops.AddRange(future);
      owners.AddRange(future.Select(_ => next));
      ids.Add(next.Id);
      previous = load;
    }
    notes.Add(
      plan.ExecutionLegId.HasValue && !EndsAtDelivery(loads[^1])
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
    return new(route, stops, currentStopCount, ids, Signature(loads), notes)
    {
      Itinerary = itinerary,
      History = history.ToImmutable(),
      Roads = roads.ToImmutable(),
      StartAccessMiles = startAccessMiles,
      DispatchSignatures = loads
        .Where(x => ids.Contains(x.Id))
        .ToDictionary(x => x.Id, LoadSignature),
    };
  }

  public static List<T> SelectLoads<T>(RoutePlan plan, IReadOnlyList<T> loads)
    where T : IWorkFacts
  {
    if (plan.ExecutionLegId.HasValue)
    {
      var current = loads
        .Where(x =>
          x.Id == plan.DispatchId
          && x.TruckId == plan.TruckId
          && x.ExecutionLegId == plan.ExecutionLegId
          && x.AssignmentRevision == plan.AssignmentRevision
          && PlanningWorkPolicy.CanUseGps(x)
          && !x.AwaitingReceipt
          && !x.Stops.Any(stop =>
            !stop.DriverOnly
            && stop.TruckId.HasValue
            && stop.TruckId != plan.TruckId
          )
        )
        .ToList();
      if (current.Count != 1)
        throw new RoutePlanningException(
          "The selected execution changed. Reload before finding fuel."
        );
      if (!EndsAtDelivery(current[0]))
        return current;
      var ids = new HashSet<Guid> { plan.DispatchId };
      var index = loads.ToList().IndexOf(current[0]);
      foreach (var next in loads.Skip(index + 1))
      {
        if (next.ExecutionLegId.HasValue)
        {
          if (
            next.Id == plan.DispatchId
            && next.ExecutionStatus == "planned"
            && next.TruckId == plan.TruckId
          )
            continue;
          // A load with an execution leg of its own ends the horizon, even
          // when it is this truck's next work. Chaining it was tried: the
          // route came out right - 2,528 miles over four dispatches - and
          // the plan could not be kept. A saved fuel plan is scoped to one
          // leg, and both the store and the schedule check say so in the
          // same words: every stop after the root must carry no leg and
          // revision zero. Widening that scope is a piece of work in itself,
          // not a line here.
          break;
        }
        if (
          next.TruckId != plan.TruckId
          || next.AwaitingReceipt
          || next.Stops.Any(stop =>
            !stop.DriverOnly
            && stop.TruckId.HasValue
            && stop.TruckId != plan.TruckId
          )
          || !ids.Add(next.Id)
        )
          throw new RoutePlanningException(
            "A future dispatch assignment changed. Reload before finding fuel."
          );
        current.Add(next);
        if (!EndsAtDelivery(next))
          break;
      }
      return current;
    }
    var root = loads.Where(x => x.Id == plan.DispatchId).ToArray();
    if (
      root.Length != 1
      || root[0].ExecutionLegId.HasValue
      || root[0].TruckId != plan.TruckId
      || root[0].AssignmentRevision != plan.AssignmentRevision
    )
      throw new RoutePlanningException(
        "The selected assignment changed. Reload before finding fuel."
      );
    var start = loads.ToList().IndexOf(root[0]);
    var remaining = loads.Skip(start).ToList();
    if (
      remaining.Any(x => x.ExecutionLegId.HasValue)
      || remaining.Select(x => x.Id).Distinct().Count() != remaining.Count
      || remaining.Any(x =>
        x.TruckId != plan.TruckId
        || x.AwaitingReceipt
        || x.Stops.Any(stop =>
          !stop.DriverOnly
          && stop.TruckId.HasValue
          && stop.TruckId != plan.TruckId
        )
      )
    )
      throw new RoutePlanningException(
        "This itinerary crosses an execution transfer. Select its current execution before finding fuel."
      );
    return remaining;
  }

  private static bool EndsAtDelivery(IWorkFacts load)
  {
    var last = load
      .Stops.Where(stop => !stop.DriverOnly)
      .OrderBy(stop => stop.Sequence)
      .LastOrDefault();
    var operation = last?.ManualAction ?? last?.Job;
    return string.Equals(
        operation,
        "Drop Off",
        StringComparison.OrdinalIgnoreCase
      )
      || string.Equals(
        operation,
        "Delivery",
        StringComparison.OrdinalIgnoreCase
      );
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
      return await deadheads.CaptureRouteAsync(previous.Id, next, profile, ct);
    // The captured itinerary selected this predecessor. Its execution owns
    // the delivery assignment even when the imported load retains old trucks.
    var history = new Dictionary<Guid, DeadheadHistorySnapshot>
    {
      [next.Id] = DeadheadHistoryProjection.Capture(
        new(next, [previous], false)
      ),
    };
    var saved = await deadheads.ReadCapturedRouteAsync(
      previous.Id,
      next,
      profile,
      history,
      ct
    );
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
    var saved = await db
      .DispatchBaseRoutes.AsNoTracking()
      .SingleOrDefaultAsync(
        row =>
          row.DispatchId == load.Id
          && row.ExecutionLegId == load.ExecutionLegId,
        ct
      );
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
      var stored = await db
        .DispatchRoutePlans.AsNoTracking()
        .SingleOrDefaultAsync(
          row =>
            row.DispatchId == load.Id
            && row.ExecutionLegId == load.ExecutionLegId,
          ct
        );
      var previous =
        stored?.InputHash == RoutePlanningService.HashInputs(load, profile)
        && stored.TruckId == load.TruckId
        && stored.AssignmentRevision == load.AssignmentRevision
          ? SavedRouteReader.Plan(stored.PlanJson)
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
    RequireAnchored(route, points);
    return (route, roads.ToImmutable());
  }

  private static TruckRoute RemainingExtension(
    TruckRoute route,
    IReadOnlyList<RouteWorkStop> ordered,
    IReadOnlyList<PlanStop> remaining
  )
  {
    var selected = remaining.Select(stop => stop.Id).ToHashSet();
    var legs = new List<RouteLeg>();
    var start = 0;
    for (var i = 0; i < ordered.Count; i++)
    {
      if (!selected.Contains(ordered[i].Id))
        continue;
      var part = route.Legs.Skip(start).Take(i - start + 1).ToArray();
      // Keep the saved path through completed intermediate stops without
      // inventing a shortcut.
      legs.Add(
        part.Length == 1
          ? part[0]
          : new(
            part.Sum(leg => leg.Miles),
            part.Sum(leg => leg.Seconds),
            part.SelectMany(
                (leg, index) => index == 0 ? leg.Points : leg.Points.Skip(1)
              )
              .ToList()
          )
      );
      start = i + 1;
    }
    return new()
    {
      CalculatedAt = route.CalculatedAt,
      Legs = legs,
      Miles = legs.Sum(leg => leg.Miles),
      Seconds = legs.Sum(leg => leg.Seconds),
      Warnings = [.. route.Warnings],
    };
  }

  public static TruckRoute Join(TruckRoute first, TruckRoute next)
  {
    if (
      !first.TryGetLegSeconds(out var firstSeconds)
      || !next.TryGetLegSeconds(out var nextSeconds)
    )
      throw new RoutePlanningException(
        "A saved route has inconsistent timing. Rebuild the route before finding fuel."
      );
    if (!RouteAnchoring.Continuous(first, next))
      throw new RoutePlanningException(
        "The saved route segments do not connect. Rebuild the affected connection before finding fuel."
      );
    RequirePointBudget(
      first.Legs.Sum(leg => (long)(leg.Points?.Count ?? 0))
        + next.Legs.Sum(leg => (long)(leg.Points?.Count ?? 0))
    );
    return new()
    {
      CalculatedAt =
        first.CalculatedAt < next.CalculatedAt
          ? first.CalculatedAt
          : next.CalculatedAt,
      Miles = first.Miles + next.Miles,
      Seconds = firstSeconds + nextSeconds,
      Legs = [.. first.Legs, .. next.Legs],
      Warnings = [.. first.Warnings, .. next.Warnings],
    };
  }

  private static void RequireAnchored(
    TruckRoute route,
    IReadOnlyList<RoutePoint> points
  )
  {
    if (
      !SavedRouteGeometry.Complete(route, points.Count - 1)
      || !RouteAnchoring.Matches(route, points)
    )
      throw new RoutePlanningException(
        "The fuel route does not reach the confirmed stops. The saved fuel plan has been kept."
      );
    RequirePointBudget(route.Legs.Sum(leg => (long)leg.Points.Count));
  }

  private static void RequirePointBudget(long points)
  {
    if (points > MaximumGeometryPoints)
      throw new RoutePlanningException(
        "The complete assigned route exceeds the supported geometry limit. The saved fuel plan has been kept."
      );
  }
}
