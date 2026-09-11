using Application.Features.Routing.Services.Routes;
using Application.Features.Routing.Services.Addresses;
using Application.Features.Routing.Services.Deadheads;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Queries;
using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Models;
using Domain.Entities.Dispatch;

namespace Application.Features.Routing.Services.FuelPlanning;

public sealed record FuelHorizonResult(TruckRoute Route, List<PlanStop> Stops, int CurrentStopCount, List<Guid> DispatchIds,
  string AssignmentSignature, List<string> Notes)
{
  public List<FuelItineraryStop> Itinerary { get; init; } = [];
  public Dictionary<Guid, string> DispatchSignatures { get; init; } = [];
  public double StartAccessMiles { get; init; }
}

public sealed class FuelHorizon(RoutePlanningService plans, IAppDbContext db, ISender mediator, DeadheadService deadheads)
{
  private const int MaximumGeometryPoints = 200_000;
  public static string LoadSignature(DispatchResponse load) => Convert.ToHexString(SHA256.HashData(
    JsonSerializer.SerializeToUtf8Bytes(new { load.Id, load.TruckId, Stops = load.Stops.OrderBy(s => s.Sequence).Select(s => new {
      s.Id, s.Sequence, s.TruckId, s.Address, s.City, s.Province, s.ZipCode, s.Country,
      s.Latitude, s.Longitude, s.ScheduledDate, s.ScheduledTime, s.ScheduledDate2, s.ScheduledTime2
    }) }, RoutePlanningService.Json)));

  public static string Signature(IEnumerable<DispatchResponse> loads) => Convert.ToHexString(SHA256.HashData(
    Encoding.UTF8.GetBytes(JsonSerializer.Serialize(loads.Select(x => new {
      x.Id, x.TruckId, x.Status, Stops = x.Stops.OrderBy(s => s.Sequence).Select(s => new {
        s.Sequence, s.TruckId, s.Address, s.City, s.Province, s.ZipCode, s.Country,
        s.Latitude, s.Longitude, s.ScheduledDate, s.ScheduledTime, s.ScheduledDate2, s.ScheduledTime2
      })
    }), RoutePlanningService.Json))));

  public async Task<FuelHorizonResult> BuildAsync(RoutePlanningState state, TruckRouteProfile profile, CancellationToken ct)
  {
    ct.ThrowIfCancellationRequested();
    var plan = state.Plan!;
    var board = await mediator.Send(new GetDispatchBoardQuery(TruckId: plan.TruckId, IncludeHos: false, IncludeFinancials: false, IncludeEta: false, IncludeOverdue: true), ct);
    if (!board.Success || board.Response is null)
      throw new RoutePlanningException("Dispatch assignments could not be verified. The saved fuel plan has been kept.");
    var loads = board.Response.Items.FirstOrDefault()?.Dispatches ?? [];
    var index = loads.FindIndex(x => x.Id == plan.DispatchId);
    if (index < 0) throw new RoutePlanningException("Current dispatch assignment changed. Reload the route before finding fuel.");
    var current = await plans.LoadAsync(plan.DispatchId, ct);
    var completed = current.Stops.Where(s => s.DepartedAt.HasValue || s.PickedUpAt.HasValue || s.DeliveredAt.HasValue).Select(s => s.Id).ToHashSet();
    var now = DateTime.UtcNow;
    var currentStops = current.Stops.ToDictionary(stop => stop.Id);
    var stops = plan.Stops.Where(s => !plan.Tracking.PassedStopIds.Contains(s.Id) && !completed.Contains(s.Id))
      .Select(stop => currentStops.TryGetValue(stop.Id, out var source)
        ? stop with { Point = ConfirmedPoint(source, now) }
        : throw new RoutePlanningException("Current stops changed. Reload the saved route before finding fuel.")).ToList();
    var currentStopCount = stops.Count;
    if (stops.Count == 0) throw new RoutePlanningException("No remaining dispatch stops.");
    var start = state.Progress!.Position!;
    var currentRoad = !plan.FromCurrentPosition && plan.Route.Legs.Count + 1 == plan.Stops.Count
      ? new RoutePlan { FromCurrentPosition = true, InputsChanged = plan.InputsChanged, Route = plan.Route,
        Stops = plan.Stops.Skip(1).ToList() } : plan;
    var route = RemainingFuelRoute.TryRead(currentRoad, stops, start, FuelAccessEstimate.CurrentPositionToleranceMiles);
    if (route is null)
      throw new RoutePlanningException("A matching saved current route is required before finding fuel. The saved fuel plan has been kept.");
    var origin = route.Legs[0].Points[0];
    var startAccessMiles = FuelAccessEstimate.DistanceMiles(RouteGeometry.Distance(start, origin));
    RequireAnchored(route, new[] { origin }.Concat(stops.Select(s => s.Point)).ToList());
    var ids = new List<Guid> { plan.DispatchId };
    var owners = stops.Select(_ => plan.DispatchId).ToList();
    var notes = new List<string>();
    foreach (var next in loads.Skip(index + 1))
    {
      var load = await plans.LoadAsync(next.Id, ct);
      if (load.TruckId != plan.TruckId || load.Stops.Any(s => s.TruckId.HasValue && s.TruckId != plan.TruckId))
        throw new RoutePlanningException("A future dispatch has a different truck assignment. Review assignments before calculating fuel.");
      var future = new List<PlanStop>();
      var orderedStops = load.Stops.OrderBy(s => s.Sequence).ToList();
      foreach (var stop in orderedStops.Where(s => s.DepartedAt is null && s.PickedUpAt is null && s.DeliveredAt is null))
      {
        var address = string.Join(", ", new[] { stop.Address, stop.City, stop.Province, stop.ZipCode, stop.Country }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var point = ConfirmedPoint(stop, now);
        future.Add(new(stop.Id, stop.Name, address, stop.Sequence, point) { Job = stop.Job,
          ScheduledDate = stop.ScheduledDate, ScheduledTime = stop.ScheduledTime,
          ScheduledDate2 = stop.ScheduledDate2, ScheduledTime2 = stop.ScheduledTime2 });
      }
      if (future.Count == 0) continue;
      if (stops.Count + future.Count > 40)
        throw new RoutePlanningException("The assigned itinerary exceeds 40 stops. A complete fuel plan cannot be calculated within the route limit.");
      var points = orderedStops.Select(stop => ConfirmedPoint(stop, now)).ToArray();
      var connection = await deadheads.ReadRouteAsync(ids[^1], load, profile, ct);
      if (connection is null || !RouteAnchoring.Continuous(route, connection))
        throw new RoutePlanningException("A matching saved connection to the next load is required before finding fuel. The saved fuel plan has been kept.");
      RequireAnchored(connection, [route.Legs[^1].Points[^1], points[0]]);
      var extension = points.Length == 1 ? connection
        : Join(connection, await ReadBaseAsync(load, profile, points, ct));
      if (future.Count != orderedStops.Count) extension = RemainingExtension(extension, orderedStops, future);
      // End at an actual dispatch stop; never compare variants with different endpoints.
      route = Join(route, extension);
      stops.AddRange(future);
      owners.AddRange(future.Select(_ => next.Id));
      ids.Add(next.Id);
    }
    notes.Add($"Covers {ids.Count} assigned dispatch(es), including connecting deadhead. Future stops are provisional recommendations.");
    double end = 0;
    var itinerary = stops.Select((stop, i) => new FuelItineraryStop(owners[i], stop, end += route.Legs[i].Miles)).ToList();
    return new(route, stops, currentStopCount, ids, Signature(loads), notes)
    { Itinerary = itinerary, StartAccessMiles = startAccessMiles,
      DispatchSignatures = loads.Where(x => ids.Contains(x.Id)).ToDictionary(x => x.Id, LoadSignature) };
  }

  internal static RoutePoint ConfirmedPoint(DispatchStop stop, DateTime now)
  {
    if (StopLocation.VerifiedPoint(stop, now) is { } verified) return verified;
    if (string.IsNullOrWhiteSpace(stop.Address) && stop.Latitude is { } latitude && stop.Longitude is { } longitude
      && new RoutePoint((double)latitude, (double)longitude) is { IsValid: true } exact) return exact;
    throw new RoutePlanningException("A confirmed saved stop location is required before finding fuel. The saved fuel plan has been kept.");
  }

  private async Task<TruckRoute> ReadBaseAsync(Domain.Entities.Dispatch.Dispatch load, TruckRouteProfile profile,
    IReadOnlyList<RoutePoint> points, CancellationToken ct)
  {
    var saved = await db.DispatchBaseRoutes.AsNoTracking().SingleOrDefaultAsync(row => row.DispatchId == load.Id, ct);
    var route = saved?.InputHash == BaseRouteService.Signature(load, profile)
      ? SavedRouteReader.Route(saved.RouteJson, points.Count - 1) : null;
    if (route is null)
    {
      var stored = await db.DispatchRoutePlans.AsNoTracking().SingleOrDefaultAsync(row => row.DispatchId == load.Id, ct);
      var previous = stored?.InputHash == RoutePlanningService.HashInputs(load, profile)
        && stored.TruckId == load.TruckId ? SavedRouteReader.Plan(stored.PlanJson) : null;
      if (previous is { FromCurrentPosition: false } && previous.DispatchId == load.Id && previous.TruckId == load.TruckId)
        route = previous.Route;
    }
    if (route is null)
      throw new RoutePlanningException("A matching saved base route is required for every assigned load before finding fuel. The saved fuel plan has been kept.");
    RequireAnchored(route, points);
    return route;
  }

  private static TruckRoute RemainingExtension(TruckRoute route, IReadOnlyList<DispatchStop> ordered,
    IReadOnlyList<PlanStop> remaining)
  {
    var selected = remaining.Select(stop => stop.Id).ToHashSet();
    var legs = new List<RouteLeg>();
    var start = 0;
    for (var i = 0; i < ordered.Count; i++)
    {
      if (!selected.Contains(ordered[i].Id)) continue;
      var part = route.Legs.Skip(start).Take(i - start + 1).ToArray();
      // Keep the saved path through completed intermediate stops without inventing a shortcut.
      legs.Add(part.Length == 1 ? part[0] : new(part.Sum(leg => leg.Miles), part.Sum(leg => leg.Seconds),
        part.SelectMany((leg, index) => index == 0 ? leg.Points : leg.Points.Skip(1)).ToList()));
      start = i + 1;
    }
    return new() { CalculatedAt = route.CalculatedAt, Legs = legs, Miles = legs.Sum(leg => leg.Miles),
      Seconds = legs.Sum(leg => leg.Seconds), Warnings = [.. route.Warnings] };
  }

  public static TruckRoute Join(TruckRoute first, TruckRoute next)
  {
    if (!first.TryGetLegSeconds(out var firstSeconds) || !next.TryGetLegSeconds(out var nextSeconds))
      throw new RoutePlanningException("A saved route has inconsistent timing. Rebuild the route before finding fuel.");
    if (!RouteAnchoring.Continuous(first, next))
      throw new RoutePlanningException("The saved route segments do not connect. Rebuild the affected connection before finding fuel.");
    RequirePointBudget(first.Legs.Sum(leg => (long)(leg.Points?.Count ?? 0)) + next.Legs.Sum(leg => (long)(leg.Points?.Count ?? 0)));
    return new()
    {
      CalculatedAt = first.CalculatedAt < next.CalculatedAt ? first.CalculatedAt : next.CalculatedAt,
      Miles = first.Miles + next.Miles, Seconds = firstSeconds + nextSeconds,
      Legs = [.. first.Legs, .. next.Legs],
      Warnings = [.. first.Warnings, .. next.Warnings]
    };
  }

  private static void RequireAnchored(TruckRoute route, IReadOnlyList<RoutePoint> points)
  {
    if (!SavedRouteGeometry.Complete(route, points.Count - 1) || !RouteAnchoring.Matches(route, points))
      throw new RoutePlanningException("The fuel route does not reach the confirmed stops. The saved fuel plan has been kept.");
    RequirePointBudget(route.Legs.Sum(leg => (long)leg.Points.Count));
  }

  private static void RequirePointBudget(long points)
  {
    if (points > MaximumGeometryPoints)
      throw new RoutePlanningException("The complete assigned route exceeds the supported geometry limit. The saved fuel plan has been kept.");
  }
}
