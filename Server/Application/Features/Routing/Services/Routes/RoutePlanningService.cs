using Application.Features.Routing.Services.Addresses;
using Application.Caching;
using Application.Features.Synchronization.Options;
using Application.Features.Synchronization.Services;
using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Options;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application.Features.Fleet.Models;
using Application.Features.Fleet.Queries.GetFleetLocations;
using Domain.Entities.Dispatch;
using Domain.Entities.Fleet;
using Application.Features.Routing.Models;

namespace Application.Features.Routing.Services.Routes;

public sealed class RoutePlanningService(IAppDbContext db, IRoutingProvider routing, ISender mediator, TruckPlanningProfileService profiles, RoutePlanStore store, RouteRecalculationBudget recalculationBudget, ReadCache reads, Microsoft.Extensions.Options.IOptions<FuelRegionOptions> regionOptions,
  Microsoft.Extensions.Options.IOptions<SynchronizationOptions> syncOptions, RouteDisplayCache displays, BaseRouteService baseRoutes)
{
  private static readonly KeyedGates BuildGates = new();
  public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
  internal void InvalidateReadCache(Guid dispatchId) => store.Invalidate(dispatchId);

  public async Task<RoutePlanningState> GetAsync(Guid dispatchId, CancellationToken ct, bool cachedTelemetryOnly = false)
    => await GetAsync(await LoadAsync(dispatchId, ct), ct, cachedTelemetryOnly);

  public async Task<RoutePlanningState> GetAsync(Domain.Entities.Dispatch.Dispatch load, CancellationToken ct,
    bool cachedTelemetryOnly = false, bool displayOnly = false, Guid? knownPlanId = null, int? knownVersion = null)
  {
    var dispatchId = load.Id;
    var profile = await ProfileAsync(load.TruckId!.Value, ct);
    var snapshot = displayOnly
      ? await displays.GetAsync(dispatchId, () => store.ReadUncachedAsync(dispatchId, ct), ct) : null;
    var saved = displayOnly ? snapshot?.Metadata : await store.ReadAsync(dispatchId, ct);
    var plan = snapshot is not null ? snapshot.ReadPlan(knownPlanId, knownVersion)
      : saved is null ? null : JsonSerializer.Deserialize<RoutePlan>(saved.PlanJson, Json);
    if (plan is not null)
    {
      plan.InputsChanged = !MatchesInputs(saved!, load, profile) || plan.TruckId != load.TruckId;
      plan.Profile = profile;
      if (plan.FromCurrentPosition && plan.Tracking.PassedStopIds.Count == 0)
        plan.OriginalPlannedMiles = Math.Max(plan.OriginalPlannedMiles, plan.Route.Miles);
      plan.Stops = plan.Stops.Select(stop => EnrichStop(stop, load.Stops)).ToList();
      if (plan.ReferenceStops is not null) plan.ReferenceStops = plan.ReferenceStops.Select(stop => EnrichStop(stop, load.Stops)).ToList();
      if (plan.FuelRecommendations is { } recommendations && recommendations.SettingsSignature != PlanningSettingsService.Signature(profile))
        plan.FuelRecommendations = null;
    }
    TruckLocation? truck = null;
    try { truck = await LocationAsync(load.TruckId.Value, ct, cachedTelemetryOnly); }
    catch (HttpRequestException) { }
    var progress = plan is not null ? Progress(plan, truck, load, snapshot?.Geometry) : null;
    if (plan?.FuelPlan is { } fuel)
    {
      var changed = fuel.SelectionVersion != FuelOptimizer.SelectionVersion
        || fuel.ArrivalPolicy?.PolicySignature != regionOptions.Value.Signature
        || fuel.ProfileSignature != JsonSerializer.Serialize(profile, Json)
        || plan.InputsChanged || fuel.RouteVersion != plan.Version;
      fuel.RefreshReasons = [];
      if (changed || fuel.DispatchIds.Count == 0) fuel.RefreshReasons.Add("Route or fuel settings changed.");
      if (progress?.OffRoute == true) fuel.RefreshReasons.Add("Truck is off the calculated route.");
      if (progress?.LocationStale == true) fuel.RefreshReasons.Add("Fresh GPS is needed to verify the plan.");
      if (DateTime.UtcNow - fuel.CalculatedAt > TimeSpan.FromMinutes(30))
        fuel.RefreshReasons.Add("Check current fuel prices and quantities.");
      if (progress?.ProgressMiles is { } along && truck?.FuelPercent is { } level && profile.Mpg is > 0 && profile.TankGallons is > 0)
      {
        var used = Math.Max(0, along - fuel.StartProgressMiles) / profile.Mpg!.Value;
        var expected = fuel.StartingGallons - used;
        var actual = (double)level * profile.TankGallons!.Value / 100;
        if (Math.Abs(actual - expected) > Math.Max(10, profile.TankGallons.Value * .08))
          fuel.RefreshReasons.Add("Fuel level differs from the plan. Recalculate from the latest reading.");
      }
      fuel.NeedsRefresh = fuel.RefreshReasons.Count > 0;
    }
    var state = new RoutePlanningState(profile, plan, progress,
      truck?.FuelPercent is { } f ? (double)f : null, truck?.FuelUpdatedAt, routing.IsConfigured);
    if (displayOnly) AutomaticPlanningService.ProjectRecommendations(state, snapshot?.Geometry);
    return state;
  }

  private static PlanStop EnrichStop(PlanStop stop, IEnumerable<Domain.Entities.Dispatch.DispatchStop> stops)
  {
    var source = stops.FirstOrDefault(x => x.Id == stop.Id);
    return source is null ? stop : stop with { Name = source.Name, Job = source.Job,
      ScheduledDate = source.ScheduledDate, ScheduledTime = source.ScheduledTime,
      ScheduledDate2 = source.ScheduledDate2, ScheduledTime2 = source.ScheduledTime2,
      Commodity = source.Commodity, Notes = source.Notes };
  }

  public async Task<TruckRouteProfile> SaveProfileAsync(Guid dispatchId, TruckRouteProfile profile, CancellationToken ct)
  {
    var load = await LoadAsync(dispatchId, ct);
    return await profiles.SaveAsync(load.TruckId!.Value, profile, ct);
  }

  public async Task<RoutePlan> BuildAsync(Guid dispatchId, RouteBuildRequest request, CancellationToken ct)
  {
    if (request.Profile.Validate() is { } error) throw new RoutePlanningException(error);
    var gate = BuildGates.For((await LoadAsync(dispatchId, ct)).TruckId!.Value);
    await GateWait.WaitAsync(gate, "RouteBuild", ct);
    try
    {
      var load = await LoadAsync(dispatchId, ct);
      var hash = HashInputs(load, request.Profile);
      var entity = await store.ReadForUpdateAsync(dispatchId, ct);
      var old = entity is null ? null : JsonSerializer.Deserialize<RoutePlan>(entity.PlanJson, Json);
      var ordered = load.Stops.OrderBy(x => x.Sequence).ToList();
      if (request.FromCurrentPosition)
      {
        if (!request.NextStopSequence.HasValue || !ordered.Any(x => x.Sequence == request.NextStopSequence))
          throw new RoutePlanningException("Choose the next uncompleted stop before routing from the current position.");
        ordered = ordered.Where(x => x.Sequence >= request.NextStopSequence).ToList();
      }
      if (ordered.Count < (request.FromCurrentPosition ? 1 : 2)) throw new RoutePlanningException("This load has too few stops to build a route.");
      if (ordered.Count > 49) throw new RoutePlanningException("This route supports up to 49 stops.");
      if (old is not null && MatchesInputs(entity!, load, request.Profile) && !request.FromCurrentPosition && !old.FromCurrentPosition) return old;
      if (old is not null && old.CalculatedAt > DateTime.UtcNow.AddSeconds(-60))
        throw new RoutePlanningException("The route was just calculated. Wait one minute before rebuilding it.");
      var stops = new List<PlanStop>();
      foreach (var stop in ordered)
      {
        var address = StopLocation.Address(stop);
        var point = await StopLocation.ResolveAsync(stop, routing, ct);
        stops.Add(new(stop.Id, stop.Name, address, stop.Sequence, point));
      }
      var points = stops.Select(x => x.Point).ToList();
      if (request.FromCurrentPosition)
      {
        var current = await LocationAsync(load.TruckId!.Value, ct);
        if (current is null || DateTime.UtcNow - current.UpdatedAt > TimeSpan.FromMinutes(10))
          throw new RoutePlanningException("A fresh truck GPS location is needed to route from its current position.");
        points.Insert(0, new((double)current.Latitude, (double)current.Longitude));
      }
      var route = !request.FromCurrentPosition
        ? await baseRoutes.EnsureAsync(load, request.Profile, ct)
        : await routing.CalculateAsync(points, request.Profile, ct);
      var plan = new RoutePlan { Id = entity?.Id ?? Guid.NewGuid(), DispatchId = dispatchId, TruckId = load.TruckId!.Value,
        Version = (old?.Version ?? 0) + 1, CalculatedAt = route.CalculatedAt, Profile = request.Profile,
        OriginalPlannedMiles = old?.OriginalPlannedMiles ?? route.Miles, FromCurrentPosition = request.FromCurrentPosition,
        Stops = stops, Route = route, FuelPlan = old?.FuelPlan };
      if (plan.FuelPlan is { } previousFuel) previousFuel.NeedsRefresh = true;
      await using var transaction = await db.Database.BeginTransactionAsync(ct);
      await profiles.SaveAsync(plan.TruckId, request.Profile, ct);
      await store.SaveBuiltAsync(entity, plan, hash, ct);
      await transaction.CommitAsync(ct);
      profiles.Invalidate(plan.TruckId);
      store.Invalidate(dispatchId);
      return plan;
    }
    finally { gate.Release(); }
  }

  public Task ClearFuelAsync(Guid dispatchId, CancellationToken ct) => store.ClearFuelAsync(dispatchId, ct);

  public Task<FuelPlan> StoreFuelAsync(Guid dispatchId, FuelPlan fuel, CancellationToken ct) =>
    store.StoreFuelAsync(dispatchId, fuel, ct);

  public async Task<FuelPlan> StoreFuelRouteAsync(Guid dispatchId, FuelPlan fuel, TruckRoute route, List<PlanStop> stops, double completedMiles, CancellationToken ct)
  {
    var gate = BuildGates.For((await LoadAsync(dispatchId, ct)).TruckId!.Value);
    await GateWait.WaitAsync(gate, "RouteBuild", ct);
    try
    {
      var entity = await store.ReadForUpdateAsync(dispatchId, ct) ?? throw new RoutePlanningException("Route not found.");
      var plan = JsonSerializer.Deserialize<RoutePlan>(entity.PlanJson, Json)!;
      var profile = await ProfileAsync(plan.TruckId, ct);
      var load = await LoadAsync(dispatchId, ct);
      if (plan.Version != fuel.RouteVersion || !MatchesInputs(entity, load, profile))
        throw new RoutePlanningException("Dispatch route changed during fuel planning. Retry the calculation.");
      if (fuel.ProfileSignature != JsonSerializer.Serialize(profile, Json))
        throw new RoutePlanningException("Fuel settings changed during calculation.");
      plan.ReferenceRoute ??= plan.Route;
      plan.ReferenceStops ??= plan.Stops;
      plan.Route = route;
      plan.OriginalPlannedMiles = completedMiles + route.Miles;
      plan.Stops = stops;
      plan.FromCurrentPosition = true;
      plan.CalculatedAt = route.CalculatedAt;
      plan.Version++;
      fuel.RouteVersion = plan.Version;
      plan.FuelPlan = fuel;
      plan.FuelRecommendations = null;
      plan.Tracking.OffRouteSince = null;
      await store.SaveAsync(entity, plan, ct);
      return fuel;
    }
    finally { gate.Release(); }
  }

  public async Task<bool> AdvanceAutomaticallyAsync(Guid dispatchId, CancellationToken ct, bool forceReroute = false)
  {
    var gate = BuildGates.For((await LoadAsync(dispatchId, ct)).TruckId!.Value);
    await GateWait.WaitAsync(gate, "RouteBuild", ct);
    try
    {
      var load = await LoadAsync(dispatchId, ct);
      var recent = await mediator.Send(new GetFleetLocationsQuery(), ct);
      var truck = recent.Response?.Trucks.FirstOrDefault(x => x.TruckId == load.TruckId);
      var entity = await store.ReadAsync(dispatchId, ct) ?? throw new RoutePlanningException("Route not found.");
      var plan = JsonSerializer.Deserialize<RoutePlan>(entity.PlanJson, Json)!;
      var now = DateTime.UtcNow;
      var before = JsonSerializer.Serialize(plan.Tracking, Json);
      foreach (var point in (recent.Response?.Points ?? []).Where(x => x.TruckId == load.TruckId && x.UpdatedAt <= truck?.UpdatedAt).OrderBy(x => x.UpdatedAt))
        RouteStopTracker.Update(plan, load, point, now);
      RouteStopTracker.Update(plan, load, truck, now);
      var progress = Progress(plan, truck, load);
      var fresh = progress is { LocationStale: false, Position: not null };
      var remainingStops = (plan.ReferenceStops ?? plan.Stops).Where(x => !plan.Tracking.PassedStopIds.Contains(x.Id)).ToList();
      var next = remainingStops.FirstOrDefault();
      var awayFromStop = next is not null && progress.Position is not null && RouteGeometry.Distance(progress.Position, next.Point) > 1;
      var nextSource = next is null ? null : load.Stops.FirstOrDefault(x => x.Id == next.Id);
      var deadheadToPickup = fresh && !plan.FromCurrentPosition && plan.Tracking.PassedStopIds.Count == 0
        && nextSource?.Job.Equals("Pick Up", StringComparison.OrdinalIgnoreCase) == true && awayFromStop;
      var nextIndex = plan.Stops.FindIndex(x => x.Id == next?.Id);
      var nextMile = plan.Route.Legs.Take(Math.Max(0, nextIndex + (plan.FromCurrentPosition ? 1 : 0))).Sum(x => x.Miles);
      var pendingBehind = nextIndex >= 0 && progress.ProgressMiles > nextMile + 2;
      var sync = syncOptions.Value;
      var off = fresh && !plan.Tracking.AllStopsPassed && (progress.DistanceFromRouteMiles > sync.RouteDeviationMiles || pendingBehind) && awayFromStop;
      if (!off) plan.Tracking.OffRouteSince = null;
      else plan.Tracking.OffRouteSince ??= truck!.UpdatedAt;
      var persistentDeviation = off && truck!.UpdatedAt - plan.Tracking.OffRouteSince >= TimeSpan.FromSeconds(sync.RouteDeviationSeconds);
      var cooldownPassed = plan.LastReroutedAt is null || plan.LastReroutedAt < now.AddMinutes(-5);
      var moved = plan.LastReroutePosition is null || progress.Position is not null && RouteGeometry.Distance(plan.LastReroutePosition, progress.Position) >= 1;
      if ((deadheadToPickup || persistentDeviation && cooldownPassed && moved || forceReroute && fresh && !plan.FromCurrentPosition && !plan.Tracking.AllStopsPassed) && remainingStops.Count > 0)
      {
        await recalculationBudget.ReserveAsync(plan.TruckId, progress.Position!, ct);
        var route = await routing.CalculateAsync([progress.Position!, .. remainingStops.Select(x => x.Point)], plan.Profile, ct);
        if (!plan.FromCurrentPosition && plan.Tracking.PassedStopIds.Count == 0)
          plan.OriginalPlannedMiles = route.Miles;
        plan.ReferenceRoute ??= plan.Route;
        plan.ReferenceStops ??= plan.Stops;
        plan.Route = route;
        plan.Stops = remainingStops;
        plan.FromCurrentPosition = true;
        plan.CalculatedAt = route.CalculatedAt;
        plan.LastReroutedAt = now;
        plan.LastReroutePosition = progress.Position;
        plan.Tracking.OffRouteSince = null;
        plan.Version++;
        if (plan.FuelPlan is { } previousFuel) previousFuel.NeedsRefresh = true;
        plan.FuelRecommendations = null;
      }
      if (plan.Tracking.AllStopsPassed)
      {
        plan.FuelPlan = null;
        plan.FuelRecommendations = null;
      }
      if (before != JsonSerializer.Serialize(plan.Tracking, Json) || plan.LastReroutedAt == now)
      {
        await store.SaveAsync(entity, plan, ct);
        return true;
      }
      return false;
    }
    finally { gate.Release(); }
  }

  public Task StoreRecommendationsAsync(Guid dispatchId, int version, FuelRecommendations recommendations, CancellationToken ct) =>
    store.StoreRecommendationsAsync(dispatchId, version, recommendations, ct);

  public async Task<Domain.Entities.Dispatch.Dispatch> LoadAsync(Guid id, CancellationToken ct)
  {
    Task<Domain.Entities.Dispatch.Dispatch?> Load() => db.Dispatches.AsNoTracking().Include(x => x.Stops).SingleOrDefaultAsync(x => x.Id == id, ct);
    var load = (await reads.GetAsync("dispatch", id.ToString(), Load))
      ?? throw new RoutePlanningException("Dispatch not found.");
    return await ResolveAssignmentAsync(load, ct);
  }

  internal async Task<Domain.Entities.Dispatch.Dispatch> ResolveAssignmentAsync(Domain.Entities.Dispatch.Dispatch load,
    CancellationToken ct, IReadOnlyDictionary<string, Guid>? knownTrucks = null)
  {
    if (!load.TruckId.HasValue)
    {
      var ids = load.Stops.Where(x => x.TruckId.HasValue).Select(x => x.TruckId!.Value).Distinct().ToList();
      if (ids.Count == 1) load.TruckId = ids[0];
      else if (ids.Count == 0 && !string.IsNullOrWhiteSpace(load.TruckNumber))
        load.TruckId = knownTrucks is null
          ? await db.Trucks.Where(x => x.UnitNumber == load.TruckNumber).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct)
          : knownTrucks.TryGetValue(load.TruckNumber, out var truckId) ? truckId : null;
    }
    if (!load.TruckId.HasValue || load.Stops.Any(stop => stop.TruckId.HasValue && stop.TruckId != load.TruckId))
      throw new RoutePlanningException("No unique truck assignment is available for this dispatch.");
    return load;
  }

  public async Task<TruckLocation?> LocationAsync(Guid truckId, CancellationToken ct, bool cachedOnly = false)
  {
    var fleet = await mediator.Send(new GetFleetLocationsQuery(cachedOnly), ct);
    return fleet.Response?.Trucks.FirstOrDefault(x => x.TruckId == truckId);
  }

  public Task<TruckRouteProfile> ProfileAsync(Guid truckId, CancellationToken ct) => profiles.GetAsync(truckId, ct);

  public static string HashInputs(Domain.Entities.Dispatch.Dispatch load, TruckRouteProfile profile) =>
    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
      LocationPolicy = "google-street-address-v1",
      load.TruckId, profile.HeightFeet, profile.WidthFeet, profile.LengthFeet,
      profile.WeightPounds, profile.Axles, profile.AxleWeightPounds, profile.Hazmat,
      Stops = load.Stops.OrderBy(x => x.Sequence).Select(x => new { x.Id, x.Sequence, x.TruckId, x.Latitude, x.Longitude,
        x.Address, x.City, x.Province, x.Country, x.ZipCode })
    }, Json))));

  internal static bool MatchesInputs(DispatchRoutePlan saved, Domain.Entities.Dispatch.Dispatch load, TruckRouteProfile profile)
  {
    if (saved.InputHash == HashInputs(load, profile)) return true;
    if (load.Stops.Any(s => !string.IsNullOrWhiteSpace(s.Address))) return false;
    var legacy = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
      load.TruckId, load.TrailerId, profile.HeightFeet, profile.WidthFeet, profile.LengthFeet,
      profile.WeightPounds, profile.Axles, profile.AxleWeightPounds, profile.Hazmat,
      Stops = load.Stops.OrderBy(x => x.Sequence).Select(x => new { x.Id, x.Sequence, x.Latitude, x.Longitude,
        x.Address, x.City, x.Province, x.Country, x.ZipCode })
    }, Json))));
    return saved.InputHash == legacy;
  }

  public static RouteProgress Progress(RoutePlan plan, TruckLocation? truck, Domain.Entities.Dispatch.Dispatch load, RouteGeometry? exactGeometry = null)
  {
    var stale = TruckLocationFreshness.IsStale(truck, DateTime.UtcNow);
    if (truck is null) return new(null, null, null, 0, false, true, null, null);
    var position = new RoutePoint((double)truck.Latitude, (double)truck.Longitude);
    if (!position.IsValid) return new(null, null, null, 0, false, true, truck.UpdatedAt, null);
    var geometry = exactGeometry ?? new RouteGeometry(plan.Route);
    if (plan.Tracking.AllStopsPassed && !plan.InputsChanged)
      return new(geometry.Miles, 0, 0, 0, false, stale, truck.UpdatedAt, position);
    var departed = load.Stops.Where(x => x.DepartedAt.HasValue || x.DeliveredAt.HasValue || x.PickedUpAt.HasValue)
      .Select(x => x.Id).ToHashSet();
    departed.UnionWith(plan.Tracking.PassedStopIds);
    double minimum = 0;
    for (var i = 0; i < plan.Stops.Count; i++)
    {
      if (!departed.Contains(plan.Stops[i].Id)) break;
      var legs = plan.FromCurrentPosition ? i + 1 : i;
      minimum = plan.Route.Legs.Take(legs).Sum(x => x.Miles);
    }
    var match = geometry.Match(position, Math.Max(0, minimum - .2));
    var off = match.Away > .5;
    if (stale || plan.InputsChanged) return new(null, null, null, match.Away, off, stale, truck.UpdatedAt, position);
    var remaining = Math.Max(0, geometry.Miles - match.Along);
    double time = 0, offset = 0;
    foreach (var leg in plan.Route.Legs)
    {
      time += leg.Miles > 0 ? leg.Seconds * Math.Clamp((offset + leg.Miles - match.Along) / leg.Miles, 0, 1) : 0;
      offset += leg.Miles;
    }
    return new(match.Along, remaining, time, match.Away, off, false, truck.UpdatedAt, position);
  }
}
