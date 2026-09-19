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

public sealed partial class RoutePlanningService(
  IAppDbContext db,
  IRoutingProvider routing,
  ISender mediator,
  TruckPlanningProfileService profiles,
  RoutePlanStore store,
  RouteRecalculationBudget recalculationBudget,
  ReadCache reads,
  IOptions<FuelRegionOptions> regionOptions,
  IOptions<SynchronizationOptions> syncOptions,
  RouteDisplayCache displays,
  BaseRouteService baseRoutes,
  TruckPlanningInputsReader inputs,
  PlanningWorkPublication publication
) : IPlannedRouteReader
{
  private static readonly KeyedGates BuildGates = new();

  private sealed record DispatchSource(
    DispatchEntity Load,
    bool HasNativeExecution
  );

  public static readonly JsonSerializerOptions Json = new(
    JsonSerializerDefaults.Web
  );

  public async Task<RoutePlanningState> GetAsync(
    Guid dispatchId,
    CancellationToken ct,
    bool cachedTelemetryOnly = false
  ) => await GetAsync(await LoadAsync(dispatchId, ct), ct, cachedTelemetryOnly);

  public Task<RoutePlanningState> GetAsync(
    DispatchEntity load,
    CancellationToken ct,
    bool cachedTelemetryOnly = false,
    bool displayOnly = false,
    Guid? knownPlanId = null,
    int? knownVersion = null,
    bool metadataOnly = false
  ) =>
    GetAsync(
      RouteWorkProjection.Capture(load),
      ct,
      cachedTelemetryOnly,
      displayOnly,
      knownPlanId,
      knownVersion,
      metadataOnly
    );

  // withoutProviderWait keeps the GPS eligibility rule and only limits the
  // telemetry source to the latest known observation, so an open database
  // transaction never waits for a provider.
  public async Task<RoutePlanningState> GetAsync(
    RouteWorkSnapshot load,
    CancellationToken ct,
    bool cachedTelemetryOnly = false,
    bool displayOnly = false,
    Guid? knownPlanId = null,
    int? knownVersion = null,
    bool metadataOnly = false,
    bool withoutProviderWait = false
  )
  {
    var dispatchId = load.Id;
    var profile = await ProfileAsync(load.TruckId!.Value, ct);
    var snapshot = displayOnly
      ? await displays.GetAsync(
        dispatchId,
        () => store.ReadUncachedAsync(dispatchId, ct, load.ExecutionLegId),
        ct,
        load.ExecutionLegId
      )
      : null;
    var saved = displayOnly
      ? snapshot?.Metadata
      : await store.ReadAsync(dispatchId, ct, load.ExecutionLegId);
    var plan = snapshot is not null
      ? metadataOnly
        ? snapshot.ReadMetadata()
        : snapshot.ReadPlan(knownPlanId, knownVersion)
      : saved is null
        ? null
        : JsonSerializer.Deserialize<RoutePlan>(saved.PlanJson, Json);
    if (plan is not null)
    {
      plan.InputsChanged =
        !MatchesInputs(saved!, load, profile) || plan.TruckId != load.TruckId;
      plan.Profile = profile;
      if (!metadataOnly && !plan.GeometryOmitted && !plan.InputsChanged)
        await AddDisplayReferenceAsync(plan, load, ct);
      if (plan.FromCurrentPosition && plan.Tracking.PassedStopIds.Count == 0)
        plan.OriginalPlannedMiles = Math.Max(
          plan.OriginalPlannedMiles,
          plan.Route.Miles
        );
      if (
        plan.FuelRecommendations is { AccessProblem: true } access
        && (
          plan.InputsChanged
          || access.Version != plan.Version
          || access.CalculatedAt < DateTime.UtcNow.AddMinutes(-5)
        )
      )
        plan.FuelRecommendations = null;
      plan.Stops = plan
        .Stops.Select(stop => EnrichStop(stop, load.Stops))
        .ToList();
      if (plan.ReferenceStops is not null)
        plan.ReferenceStops = plan
          .ReferenceStops.Select(stop => EnrichStop(stop, load.Stops))
          .ToList();
      if (
        plan.FuelRecommendations is { } recommendations
        && recommendations.SettingsSignature
          != PlanningSettingsService.Signature(profile)
      )
        plan.FuelRecommendations = null;
    }
    TruckLocation? truck = null;
    try
    {
      if (PlanningWorkPolicy.CanUseGps(load) || cachedTelemetryOnly)
        truck = await LocationAsync(
          load.TruckId.Value,
          ct,
          cachedTelemetryOnly || withoutProviderWait
        );
    }
    catch (HttpRequestException) { }
    var progress =
      plan is not null
      && (load.ExecutionStatus is not "planned" || plan.FromCurrentPosition)
        ? Progress(plan, truck, load, snapshot?.Geometry)
        : null;
    if (plan?.FuelPlan is { } fuel)
    {
      var changed =
        fuel.SelectionVersion != FuelOptimizer.SelectionVersion
        || fuel.ArrivalPolicy?.PolicySignature != regionOptions.Value.Signature
        || fuel.ProfileSignature != JsonSerializer.Serialize(profile, Json)
        || plan.InputsChanged
        || fuel.RouteVersion != plan.Version;
      fuel.RefreshReasons = [];
      // A reason that invalidates the plan hides it; one that only ages its
      // prices, or leaves the truck's position unknown, keeps it readable.
      // All of them still ask for a recalculation.
      var invalid = false;
      var priced = false;
      var unverified = false;
      if (changed || fuel.DispatchIds.Count == 0)
      {
        fuel.RefreshReasons.Add("Route or fuel settings changed.");
        invalid = true;
      }
      if (progress?.OffRoute == true)
      {
        fuel.RefreshReasons.Add("Truck is off the calculated route.");
        invalid = true;
      }
      if (progress?.LocationStale == true)
      {
        // An old GPS fix says how far along he is is unknown. It does not say
        // where he has to fuel: the stations, volumes and prices are
        // unchanged. Hiding the plan leaves the driver with no fuel stop at
        // all, which is the worse answer.
        fuel.RefreshReasons.Add("Fresh GPS is needed to verify the plan.");
        unverified = true;
      }
      if (DateTime.UtcNow - fuel.CalculatedAt > TimeSpan.FromMinutes(30))
      {
        fuel.RefreshReasons.Add("Check current fuel prices and quantities.");
        priced = true;
      }
      if (
        progress?.ProgressMiles is { } along
        && truck?.FuelPercent is { } level
        && profile.Mpg is > 0
        && profile.TankGallons is > 0
      )
      {
        var used =
          Math.Max(0, along - fuel.StartProgressMiles) / profile.Mpg!.Value;
        var expected = fuel.StartingGallons - used;
        var actual = (double)level * profile.TankGallons!.Value / 100;
        if (
          Math.Abs(actual - expected)
          > Math.Max(10, profile.TankGallons.Value * .08)
        )
        {
          fuel.RefreshReasons.Add(
            "Fuel level differs from the plan. Recalculate from the latest reading."
          );
          invalid = true;
        }
      }
      fuel.NeedsRefresh = fuel.RefreshReasons.Count > 0;
      fuel.PricesOutOfDate = !invalid && (priced || fuel.PricesOutOfDate);
      fuel.PositionUnverified =
        !invalid && (unverified || fuel.PositionUnverified);
    }
    var state = new RoutePlanningState(
      profile,
      plan,
      progress,
      truck?.FuelPercent is { } f ? (double)f : null,
      truck?.FuelUpdatedAt,
      routing.IsConfigured
    )
    {
      RouteChoiceRevision = load.RouteChoiceRevision,
      SavedRoad =
        !displayOnly && saved is not null && plan is not null
          ? SavedRoadVersion.Plan(saved, plan)
          : null,
    };
    if (
      plan?.FuelRecommendations is { AccessProblem: true } accessResult
      && !accessResult.MatchesTelemetry(state)
    )
      plan.FuelRecommendations = null;
    if (displayOnly)
      AutomaticPlanningService.ProjectRecommendations(
        state,
        snapshot?.Geometry
      );
    return state;
  }

  private static PlanStop EnrichStop(
    PlanStop stop,
    IEnumerable<RouteWorkStop> stops
  )
  {
    var source = stops.FirstOrDefault(x => x.Id == stop.Id);
    return source is null
      ? stop
      : stop with
      {
        Name = source.Name,
        Job = source.Job,
        StateAfter = source.StateAfter,
        ScheduledDate = source.ScheduledDate,
        ScheduledTime = source.ScheduledTime,
        ScheduledDate2 = source.ScheduledDate2,
        ScheduledTime2 = source.ScheduledTime2,
        AppointmentTimeZoneId = source.AppointmentTimeZoneId,
        Commodity = source.Commodity,
        Notes = source.Notes,
      };
  }

  public async Task<TruckRouteProfile> SaveProfileAsync(
    Guid dispatchId,
    TruckRouteProfile profile,
    CancellationToken ct,
    Guid? executionLegId = null,
    long? assignmentRevision = null
  )
  {
    var (work, legId) = await CaptureWorkAsync(dispatchId, ct, executionLegId);
    var load = PlanningWorkPolicy.Resolve(work, dispatchId, legId);
    if (
      assignmentRevision.HasValue
      && load.AssignmentRevision != assignmentRevision.Value
    )
      throw new RoutePlanningException(
        "The truck assignment changed. Refresh before saving its profile."
      );
    await using var transaction = await publication.BeginAsync(work, ct);
    var saved = await profiles.SaveAsync(work.TruckId, profile, ct);
    await transaction.CommitAsync(ct);
    profiles.Invalidate(work.TruckId);
    return saved;
  }

  internal async Task<(
    TruckItinerarySnapshot Itinerary,
    Guid? ExecutionLegId
  )> CaptureWorkAsync(
    Guid dispatchId,
    CancellationToken ct,
    Guid? executionLegId = null,
    Guid? truckId = null
  )
  {
    var located = await LoadAsync(dispatchId, ct, executionLegId, truckId);
    if (truckId.HasValue && located.TruckId != truckId)
      throw new RoutePlanningException("The truck assignment changed.");
    var captured =
      await inputs.ReadFreshAsync(located.TruckId!.Value, ct)
      ?? throw new RoutePlanningException("The assigned truck is unavailable.");
    PlanningWorkPolicy.Resolve(
      captured.Itinerary,
      dispatchId,
      located.ExecutionLegId
    );
    return (captured.Itinerary, located.ExecutionLegId);
  }

  public async Task<RoutePlan> BuildAsync(
    Guid dispatchId,
    RouteBuildRequest request,
    CancellationToken ct,
    bool automatic = false,
    TruckItinerarySnapshot? capturedWork = null
  )
  {
    if (request.Profile.Validate() is { } error)
      throw new RoutePlanningException(error);
    var work = capturedWork;
    if (work is null)
    {
      var captured = await CaptureWorkAsync(
        dispatchId,
        ct,
        request.ExecutionLegId
      );
      work = captured.Itinerary;
      request = request with { ExecutionLegId = captured.ExecutionLegId };
    }
    var gate = BuildGates.For(work.TruckId);
    await GateWait.WaitAsync(gate, "RouteBuild", ct);
    try
    {
      await inputs.RequireCurrentAsync(work, ct);
      var observedProfile = automatic
        ? request.Profile
        : await profiles.GetUncachedAsync(work.TruckId, ct);
      if (automatic)
        await profiles.RequireCurrentAsync(work.TruckId, observedProfile, ct);
      var load = PlanningWorkPolicy.Resolve(
        work,
        dispatchId,
        request.ExecutionLegId
      );
      if (request.FromCurrentPosition && !PlanningWorkPolicy.CanUseGps(load))
        throw new RoutePlanningException(
          "Confirm receipt before routing this leg from the truck GPS."
        );
      var hash = HashInputs(load, request.Profile);
      var entity = await store.ReadForUpdateAsync(
        dispatchId,
        ct,
        load.ExecutionLegId
      );
      var old = entity is null
        ? null
        : JsonSerializer.Deserialize<RoutePlan>(entity.PlanJson, Json);
      var ordered = load.Stops.OrderBy(x => x.Sequence).ToList();
      if (request.FromCurrentPosition)
      {
        if (
          !request.NextStopSequence.HasValue
          || !ordered.Any(x => x.Sequence == request.NextStopSequence)
        )
          throw new RoutePlanningException(
            "Choose the next uncompleted stop before routing from the current position."
          );
        ordered = ordered
          .Where(x => x.Sequence >= request.NextStopSequence && !x.IsCompleted)
          .ToList();
      }
      if (ordered.Count < (request.FromCurrentPosition ? 1 : 2))
        throw new RoutePlanningException(
          "This load has too few stops to build a route."
        );
      if (ordered.Count > 49)
        throw new RoutePlanningException("This route supports up to 49 stops.");
      if (
        old is not null
        && MatchesInputs(entity!, load, request.Profile)
        && !request.FromCurrentPosition
        && !old.FromCurrentPosition
      )
        return old;
      if (
        old is not null
        && MatchesInputs(entity!, load, request.Profile)
        && old.CalculatedAt > DateTime.UtcNow.AddSeconds(-60)
      )
        throw new RoutePlanningException(
          "The route was just calculated. Wait one minute before rebuilding it."
        );
      var stops = new List<PlanStop>();
      var locations = await StopLocation.ResolveAsync(
        load.Id,
        load.ExecutionLegId,
        ordered,
        db,
        routing,
        ct
      );
      var capturedStops = load.Stops;
      foreach (var stop in ordered)
      {
        var address = StopLocation.Address(stop);
        var point = locations[stop.Id];
        stops.Add(
          EnrichStop(
            new(stop.Id, stop.Name, address, stop.Sequence, point),
            capturedStops
          )
        );
      }
      var points = stops.Select(x => x.Point).ToList();
      if (request.FromCurrentPosition)
      {
        var current = await LocationAsync(load.TruckId!.Value, ct);
        if (
          current is null
          || DateTime.UtcNow - current.UpdatedAt > TimeSpan.FromMinutes(10)
        )
          throw new RoutePlanningException(
            "A fresh truck GPS location is needed to route from its current position."
          );
        points.Insert(
          0,
          new((double)current.Latitude, (double)current.Longitude)
        );
        if (automatic)
          await recalculationBudget.ReserveAsync(
            load.TruckId.Value,
            points[0],
            ct
          );
      }
      var route = !request.FromCurrentPosition
        ? await baseRoutes.EnsureForBuildAsync(
          load,
          request.Profile,
          observedProfile,
          work,
          ct
        )
        : await baseRoutes.CurrentAsync(
          load,
          request.Profile,
          points[0],
          stops,
          ct
        );
      TruckRoute? reference = null;
      if (request.FromCurrentPosition)
      {
        if (old is not null && MatchesInputs(entity!, load, request.Profile))
          reference =
            old.ReferenceRoute ?? (old.FromCurrentPosition ? null : old.Route);
        if (reference is null)
        {
          reference = await ReadReferenceAsync(load, request.Profile, ct);
        }
      }
      var plan = new RoutePlan
      {
        Id = entity?.Id ?? Guid.NewGuid(),
        DispatchId = dispatchId,
        ExecutionLegId = load.ExecutionLegId,
        AssignmentRevision = load.AssignmentRevision,
        TruckId = load.TruckId!.Value,
        Version = (old?.Version ?? 0) + 1,
        CalculatedAt = route.CalculatedAt,
        Profile = request.Profile,
        OriginalPlannedMiles = old?.OriginalPlannedMiles ?? route.Miles,
        FromCurrentPosition = request.FromCurrentPosition,
        Stops = stops,
        Route = route,
        ReferenceRoute = reference,
        FuelPlan = old?.FuelPlan,
      };
      if (plan.FromCurrentPosition)
      {
        await AddDisplayReferenceAsync(plan, load, ct);
        plan.ReferenceRoute = await RouteDisplayReference.ReconnectAsync(
          plan.ReferenceRoute,
          plan.ReferenceStops,
          plan.Stops,
          plan.Route,
          plan.Profile,
          routing,
          ct
        );
      }
      if (plan.FuelPlan is { } previousFuel)
        previousFuel.NeedsRefresh = true;
      await using var transaction = await publication.BeginAsync(work, ct);
      await profiles.RequireCurrentAsync(work.TruckId, observedProfile, ct);
      await profiles.SaveAsync(plan.TruckId, request.Profile, ct);
      await store.SaveBuiltAsync(entity, plan, hash, ct);
      await transaction.CommitAsync(ct);
      profiles.Invalidate(plan.TruckId);
      store.Invalidate(dispatchId, load.ExecutionLegId);
      return plan;
    }
    finally
    {
      gate.Release();
    }
  }

  public async Task<bool> AdvanceAutomaticallyAsync(
    Guid dispatchId,
    CancellationToken ct,
    bool forceReroute = false,
    Guid? executionLegId = null,
    Guid? truckId = null,
    TruckItinerarySnapshot? capturedWork = null
  )
  {
    var work = capturedWork;
    if (work is null)
      (work, executionLegId) = await CaptureWorkAsync(
        dispatchId,
        ct,
        executionLegId,
        truckId
      );
    var gate = BuildGates.For(work.TruckId);
    await GateWait.WaitAsync(gate, "RouteBuild", ct);
    try
    {
      await inputs.RequireCurrentAsync(work, ct);
      var load = PlanningWorkPolicy.Resolve(work, dispatchId, executionLegId);
      var profile = await ProfileAsync(work.TruckId, ct);
      if (
        !await PlanningWorkPolicy.IsCurrentAsync(work, load, store, profile, ct)
      )
        return false;
      var recent = await mediator.Send(new GetFleetLocationsQuery(), ct);
      var truck = recent.Response?.Trucks.FirstOrDefault(x =>
        x.TruckId == load.TruckId
      );
      var entity =
        await store.ReadAsync(dispatchId, ct, load.ExecutionLegId)
        ?? throw new RoutePlanningException("Route not found.");
      var plan = JsonSerializer.Deserialize<RoutePlan>(entity.PlanJson, Json)!;
      if (
        entity.TruckId != work.TruckId
        || plan.TruckId != work.TruckId
        || !MatchesInputs(entity, load, profile)
      )
        throw new RoutePlanningException(
          "The saved route no longer matches the truck work. Rebuild it first."
        );
      var now = DateTime.UtcNow;
      var before = JsonSerializer.Serialize(plan.Tracking, Json);
      foreach (
        var point in (recent.Response?.Points ?? [])
          .Where(x =>
            x.TruckId == load.TruckId && x.UpdatedAt <= truck?.UpdatedAt
          )
          .OrderBy(x => x.UpdatedAt)
      )
        RouteStopTracker.Update(plan, load, point, now);
      RouteStopTracker.Update(plan, load, truck, now);
      var progress = Progress(plan, truck, load);
      var fresh = progress is { LocationStale: false, Position: not null };
      var remainingStops = (plan.ReferenceStops ?? plan.Stops)
        .Where(x => !plan.Tracking.PassedStopIds.Contains(x.Id))
        .ToList();
      var next = remainingStops.FirstOrDefault();
      var awayFromStop =
        next is not null
        && progress.Position is not null
        && RouteGeometry.Distance(progress.Position, next.Point) > 1;
      var nextSource = next is null
        ? null
        : load.Stops.FirstOrDefault(x => x.Id == next.Id);
      var deadheadToPickup =
        fresh
        && !plan.FromCurrentPosition
        && plan.Tracking.PassedStopIds.Count == 0
        && nextSource?.Job.Equals("Pick Up", StringComparison.OrdinalIgnoreCase)
          == true
        && awayFromStop;
      var nextIndex = plan.Stops.FindIndex(x => x.Id == next?.Id);
      var nextMile = plan
        .Route.Legs.Take(
          Math.Max(0, nextIndex + (plan.FromCurrentPosition ? 1 : 0))
        )
        .Sum(x => x.Miles);
      var pendingBehind =
        nextIndex >= 0 && progress.ProgressMiles > nextMile + 2;
      var sync = syncOptions.Value;
      var off =
        fresh
        && !plan.Tracking.AllStopsPassed
        && (
          progress.DistanceFromRouteMiles > sync.RouteDeviationMiles
          || pendingBehind
        )
        && awayFromStop;
      if (!off)
        plan.Tracking.OffRouteSince = null;
      else
        plan.Tracking.OffRouteSince ??= truck!.UpdatedAt;
      var persistentDeviation =
        off
        && truck!.UpdatedAt - plan.Tracking.OffRouteSince
          >= TimeSpan.FromSeconds(sync.RouteDeviationSeconds);
      var cooldownPassed =
        plan.LastReroutedAt is null || plan.LastReroutedAt < now.AddMinutes(-5);
      var moved =
        plan.LastReroutePosition is null
        || progress.Position is not null
          && RouteGeometry.Distance(plan.LastReroutePosition, progress.Position)
            >= 1;
      if (
        (
          deadheadToPickup
          || persistentDeviation && cooldownPassed && moved
          || forceReroute
            && fresh
            && !plan.FromCurrentPosition
            && !plan.Tracking.AllStopsPassed
        )
        && remainingStops.Count > 0
      )
      {
        await recalculationBudget.ReserveAsync(
          plan.TruckId,
          progress.Position!,
          ct
        );
        var route = await baseRoutes.CurrentAsync(
          load,
          plan.Profile,
          progress.Position!,
          remainingStops,
          ct
        );
        if (!plan.FromCurrentPosition && plan.Tracking.PassedStopIds.Count == 0)
          plan.OriginalPlannedMiles = route.Miles;
        plan.ReferenceRoute ??= plan.Route;
        plan.ReferenceStops ??= plan.Stops;
        plan.ReferenceRoute = await RouteDisplayReference.ReconnectAsync(
          plan.ReferenceRoute,
          plan.ReferenceStops,
          remainingStops,
          route,
          plan.Profile,
          routing,
          ct
        );
        plan.Route = route;
        plan.Stops = remainingStops;
        plan.FromCurrentPosition = true;
        plan.CalculatedAt = route.CalculatedAt;
        plan.LastReroutedAt = now;
        plan.LastReroutePosition = progress.Position;
        plan.Tracking.OffRouteSince = null;
        plan.Version++;
        if (plan.FuelPlan is { } previousFuel)
          previousFuel.NeedsRefresh = true;
        plan.FuelRecommendations = null;
      }
      if (plan.Tracking.AllStopsPassed)
      {
        plan.FuelPlan = null;
        plan.FuelRecommendations = null;
      }
      if (
        before != JsonSerializer.Serialize(plan.Tracking, Json)
        || plan.LastReroutedAt == now
      )
      {
        await using var transaction = await publication.BeginAsync(work, ct);
        await profiles.RequireRoutingCurrentAsync(load, profile, ct);
        await store.SaveAsync(entity, plan, ct);
        await transaction.CommitAsync(ct);
        store.Invalidate(dispatchId, load.ExecutionLegId);
        return true;
      }
      return false;
    }
    finally
    {
      gate.Release();
    }
  }

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

  public static string HashInputs(
    DispatchEntity load,
    TruckRouteProfile profile
  ) => HashInputs(RouteWorkProjection.Capture(load.TruckItinerary()), profile);

  public static string HashInputs(
    RouteWorkSnapshot load,
    TruckRouteProfile profile
  )
  {
    load = RouteWorkProjection.TruckItinerary(load);
    return load.ExecutionLegId is { } legId
        ? Convert.ToHexString(
          SHA256.HashData(
            Encoding.UTF8.GetBytes(
              $"{legId}:{load.AssignmentRevision}:{HashItinerary(load, profile)}"
            )
          )
        )
      : load.RouteChoiceRevision == 0 ? HashItinerary(load, profile)
      : Convert.ToHexString(
        SHA256.HashData(
          Encoding.UTF8.GetBytes(
            $"{HashItinerary(load, profile)}:{load.RouteChoiceRevision}"
          )
        )
      );
  }

  private static string HashItinerary(
    RouteWorkSnapshot load,
    TruckRouteProfile profile
  ) =>
    StopCompletionIdentity.Revise(
      Convert.ToHexString(
        SHA256.HashData(
          Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(
              new
              {
                LocationPolicy = "google-street-address-v1",
                load.TruckId,
                profile.HeightFeet,
                profile.WidthFeet,
                profile.LengthFeet,
                profile.WeightPounds,
                profile.Axles,
                profile.AxleWeightPounds,
                profile.Hazmat,
                Stops = load
                  .Stops.OrderBy(x => x.Sequence)
                  .Select(x => new
                  {
                    x.Id,
                    x.Sequence,
                    x.TruckId,
                    x.Latitude,
                    x.Longitude,
                    x.Address,
                    x.City,
                    x.Province,
                    x.Country,
                    x.ZipCode,
                  }),
              },
              Json
            )
          )
        )
      ),
      load.Stops.Select(s => (s.Id, s.ManualCompletionRevision))
    );

  internal static bool MatchesInputs(
    DispatchRoutePlan saved,
    DispatchEntity load,
    TruckRouteProfile profile
  ) => MatchesInputs(saved, RouteWorkProjection.Capture(load), profile);

  internal static bool MatchesInputs(
    DispatchRoutePlan saved,
    RouteWorkSnapshot load,
    TruckRouteProfile profile
  )
  {
    if (saved.ExecutionLegId != load.ExecutionLegId)
      return false;
    if (saved.InputHash == HashInputs(load, profile))
      return true;
    if (load.ExecutionLegId.HasValue)
      return false;
    if (load.RouteChoiceRevision != 0)
      return false;
    if (load.Stops.Any(s => s.ManualCompletionRevision != 0))
      return false;
    if (load.Stops.Any(s => !string.IsNullOrWhiteSpace(s.Address)))
      return false;
    var legacy = Convert.ToHexString(
      SHA256.HashData(
        Encoding.UTF8.GetBytes(
          JsonSerializer.Serialize(
            new
            {
              load.TruckId,
              load.TrailerId,
              profile.HeightFeet,
              profile.WidthFeet,
              profile.LengthFeet,
              profile.WeightPounds,
              profile.Axles,
              profile.AxleWeightPounds,
              profile.Hazmat,
              Stops = load
                .Stops.OrderBy(x => x.Sequence)
                .Select(x => new
                {
                  x.Id,
                  x.Sequence,
                  x.Latitude,
                  x.Longitude,
                  x.Address,
                  x.City,
                  x.Province,
                  x.Country,
                  x.ZipCode,
                }),
            },
            Json
          )
        )
      )
    );
    return saved.InputHash == legacy;
  }

  public static RouteProgress Progress(
    RoutePlan plan,
    TruckLocation? truck,
    DispatchEntity load,
    RouteGeometry? exactGeometry = null
  ) => Progress(plan, truck, RouteWorkProjection.Capture(load), exactGeometry);

  public static RouteProgress Progress(
    RoutePlan plan,
    TruckLocation? truck,
    RouteWorkSnapshot load,
    RouteGeometry? exactGeometry = null
  )
  {
    var stale = TruckLocationFreshness.IsStale(truck, DateTime.UtcNow);
    if (truck is null)
      return new(null, null, null, 0, false, true, null, null);
    var position = new RoutePoint(
      (double)truck.Latitude,
      (double)truck.Longitude
    );
    if (!position.IsValid)
      return new(null, null, null, 0, false, true, truck.UpdatedAt, null);
    var geometry = exactGeometry ?? new RouteGeometry(plan.Route);
    if (plan.Tracking.AllStopsPassed && !plan.InputsChanged)
      return new(
        geometry.Miles,
        0,
        0,
        0,
        false,
        stale,
        truck.UpdatedAt,
        position
      );
    var departed = load
      .Stops.Where(x => x.IsCompleted)
      .Select(x => x.Id)
      .ToHashSet();
    departed.UnionWith(plan.Tracking.PassedStopIds);
    double minimum = 0;
    for (var i = 0; i < plan.Stops.Count; i++)
    {
      if (!departed.Contains(plan.Stops[i].Id))
        break;
      var legs = plan.FromCurrentPosition ? i + 1 : i;
      minimum = plan.Route.Legs.Take(legs).Sum(x => x.Miles);
    }
    var match = geometry.Match(position, Math.Max(0, minimum - .2));
    var off = match.Away > .5;
    if (stale || plan.InputsChanged)
      return new(
        null,
        null,
        null,
        match.Away,
        off,
        stale,
        truck.UpdatedAt,
        position
      );
    var remaining = Math.Max(0, geometry.Miles - match.Along);
    double time = 0,
      offset = 0;
    foreach (var leg in plan.Route.Legs)
    {
      time +=
        leg.Miles > 0
          ? leg.Seconds
            * Math.Clamp((offset + leg.Miles - match.Along) / leg.Miles, 0, 1)
          : 0;
      offset += leg.Miles;
    }
    return new(
      match.Along,
      remaining,
      time,
      match.Away,
      off,
      false,
      truck.UpdatedAt,
      position
    );
  }

  // The contract keeps only what a consumer outside the route lifecycle
  // needs; the wider overloads stay internal to routing.
  Task<RoutePlanningState> IPlannedRouteReader.GetAsync(
    RouteWorkSnapshot work,
    CancellationToken ct,
    PlannedRouteTelemetry telemetry
  ) =>
    GetAsync(
      work,
      ct,
      cachedTelemetryOnly: telemetry is PlannedRouteTelemetry.Cached,
      withoutProviderWait: telemetry
        is PlannedRouteTelemetry.WithoutProviderWait
    );

  Task<RouteWorkSnapshot> IPlannedRouteReader.LoadAsync(
    Guid dispatchId,
    CancellationToken ct,
    Guid? executionLegId
  ) => LoadAsync(dispatchId, ct, executionLegId);
}
