using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Queries;
using Application.Features.Eta.Algorithms;
using Application.Features.Eta.Interfaces;
using Application.Features.Eta.Models;
using Application.Features.Eta.Options;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Models;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Services.Deadheads;
using Application.Features.Routing.Services.Routes;
using Microsoft.Extensions.Options;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Application.Features.Eta.Services;

public sealed record EtaChainDescription(Guid TruckId, Guid RootDispatchId, string DriverExternalId, Guid? DriverId,
  string InputHash, string GeometryHash, IReadOnlyList<Load> Loads, TruckRouteProfile Profile,
  IReadOnlyDictionary<Guid, DeadheadConnection?> Connections);

public sealed record EtaFutureTiming(Guid DispatchId, EtaRouteTiming? Connection, EtaRouteTiming? Route,
  ImmutableArray<RoutePoint> StopPoints, string? UnavailableReason);

public sealed class EtaChainInputsService(IAppDbContext db, ISender mediator, RoutePlanningService routes,
  IEtaRootRouteReader rootRoutes, INextLoadRouteReader savedRoutes, IDeadheadHistoryReader history,
  EtaMemory memory, IRouteRegionLookup regions, IOptions<EtaPlanningOptions> options)
{
  public async Task<EtaChainDescription?> DescribeAsync(Guid truckId, CancellationToken ct,
    IReadOnlyList<DispatchResponse>? ordered = null)
  {
    if (ordered is null)
    {
      var board = await mediator.Send(new GetDispatchBoardQuery(TruckId: truckId,
        IncludeHos: false, IncludeFinancials: false, IncludeEta: false), ct);
      ordered = board.Response?.Items.FirstOrDefault(x => x.TruckId == truckId)?.Dispatches;
    }
    if (ordered is null || ordered.Count == 0) return null;
    var ids = ordered.Select(x => x.Id).ToArray();
    var loaded = await db.Dispatches.AsNoTracking().Include(x => x.Stops)
      .Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
    var profile = await routes.ProfileAsync(truckId, ct);
    var loads = new List<Load>();
    object? rootVersion = null;
    foreach (var item in ordered)
    {
      if (!loaded.TryGetValue(item.Id, out var load)) continue;
      try { load = await routes.ResolveAssignmentAsync(load, ct); }
      catch (RoutePlanningException) { break; }
      if (load.TruckId != truckId || load.Stops.Any(s => s.TruckId.HasValue && s.TruckId != truckId)) break;
      if (loads.Count == 0)
      {
        var saved = await rootRoutes.ReadAsync(load.Id, ct);
        var matches = saved is not null && saved.TruckId == truckId && saved.PlanTruckId == truckId
          && saved.InputHash == RoutePlanningService.HashInputs(load, profile);
        if (matches && saved?.Tracking.AllStopsPassed == true) continue;
        rootVersion = new { saved?.InputHash, saved?.TruckId, saved?.PlanTruckId, Id = saved?.PlanId,
          saved?.Version, saved?.Tracking, Matches = matches };
      }
      loads.Add(load);
    }
    if (loads.Count == 0) return null;
    var future = loads.Skip(1).ToArray();
    var futureIds = future.Select(x => x.Id).ToArray();
    var versions = await savedRoutes.ReadVersionsAsync(futureIds, ct);
    var predecessors = await history.ReadLoadedAsync(future, ct);
    var connections = future.ToDictionary(x => x.Id, x => DeadheadConnection.Find(predecessors.GetValueOrDefault(x.Id)));
    var driver = await db.Trucks.AsNoTracking().Where(x => x.Id == truckId)
      .Select(x => new { x.DriverId, ExternalId = x.Driver == null ? "" : x.Driver.ExternalId }).SingleOrDefaultAsync(ct);
    var geometryHash = Hash(new { TruckId = truckId, Root = loads[0].Id, Versions = versions.OrderBy(x => x.DispatchId),
      Future = future.Select(x => new { x.Id, Base = BaseRouteService.Signature(x, profile),
        Connection = connections[x.Id]?.Signature(profile), Stops = x.Stops.OrderBy(s => s.Sequence).Select(s => s.Id) }) });
    var inputHash = Hash(new { Policy = 9, TruckId = truckId, Driver = driver, rootVersion, Geometry = geometryHash,
      Planning = options.Value, Loads = loads.Select(x => new { x.Id, x.TruckId, x.DriverId, x.Status,
        Stops = x.Stops.OrderBy(s => s.Sequence).Select(s => new { s.Id, s.Sequence, s.TruckId, s.DriverId, s.Job,
          s.ScheduledDate, s.ScheduledTime, s.ScheduledDate2, s.ScheduledTime2,
          s.ArrivedAt, s.PickedUpAt, s.DeliveredAt, s.DepartedAt }) }) });
    return new(truckId, loads[0].Id, driver?.ExternalId ?? "", driver?.DriverId, inputHash, geometryHash, loads, profile, connections);
  }

  public async Task<EtaChainPlan> PrepareAsync(EtaChainDescription description, CancellationToken ct)
  {
    var timings = await memory.FutureTimingAsync(description.GeometryHash, async () =>
    {
      var future = description.Loads.Skip(1).ToArray();
      var saved = await savedRoutes.ReadGeometryAsync(future.Select(x => x.Id).ToArray(), ct);
      var values = new List<EtaFutureTiming>();
      var previous = description.RootDispatchId;
      foreach (var load in future)
      {
        var item = saved.GetValueOrDefault(load.Id);
        var pair = description.Connections.GetValueOrDefault(load.Id);
        var connection = pair?.Previous.Id == previous ? pair.ReadRoute(item?.Deadhead, description.Profile) : null;
        var road = item?.BaseRoute is { } baseRoute && baseRoute.InputHash == BaseRouteService.Signature(load, description.Profile)
          ? SavedRouteReader.Route(baseRoute.RouteJson, load.Stops.Count - 1) : null;
        string? reason = connection is null ? "ETA unavailable: waiting for the saved connection from the preceding load."
          : road is null ? "ETA unavailable: waiting for the saved load route." : null;
        var routeTiming = road is null ? null : EtaRouteTiming.Compile(road, regions);
        var connectionTiming = connection is null || connection.Legs.All(leg => leg.Miles == 0 && leg.Seconds == 0)
          ? null : EtaRouteTiming.Compile(connection, regions);
        if (routeTiming?.HasCompleteTravelTimes == false || connectionTiming?.HasCompleteTravelTimes == false)
          reason = "ETA unavailable: incomplete saved road travel times.";
        var points = road is null || road.Legs.Count == 0 ? ImmutableArray<RoutePoint>.Empty
          : road.Legs.Select(leg => leg.Points[^1]).Prepend(road.Legs[0].Points[0]).ToImmutableArray();
        values.Add(new(load.Id, connectionTiming, routeTiming, points, reason));
        previous = load.Id;
      }
      return values.ToImmutableArray();
    }, ct);
    var byId = timings.ToDictionary(x => x.DispatchId);
    var result = description.Loads.Skip(1).Select(load =>
    {
      var value = byId[load.Id];
      var stops = load.Stops.OrderBy(s => s.Sequence).Select((s, i) => new PlanStop(s.Id, s.Name, s.Address, s.Sequence,
        i < value.StopPoints.Length ? value.StopPoints[i] : new(0, 0))
        { Job = s.Job, ScheduledDate = s.ScheduledDate, ScheduledTime = s.ScheduledTime,
          ScheduledDate2 = s.ScheduledDate2, ScheduledTime2 = s.ScheduledTime2 }).ToArray();
      var driverChanged = description.DriverId.HasValue && (load.DriverId.HasValue && load.DriverId != description.DriverId
        || load.Stops.Any(s => s.DriverId.HasValue && s.DriverId != description.DriverId));
      return new EtaFutureDispatch(load.Id, stops, value.Connection, value.Route,
        driverChanged ? "ETA unavailable: the next load has a different driver assignment." : value.UnavailableReason);
    }).ToArray();
    return new(description.InputHash, result, description.Loads[0].Stops.ToDictionary(s => s.Id,
      s => new EtaStopActivity(s.ArrivedAt, s.PickedUpAt, s.DeliveredAt, s.DepartedAt)));
  }

  private static string Hash(object value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
}
