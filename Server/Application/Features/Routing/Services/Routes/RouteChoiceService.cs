using System.Text.Json;
using Application.Caching;
using Application.Concurrency;
using Application.Features.Execution.Services;
using Application.Features.Routing.Background;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Services.Addresses;
using Domain.Entities.Dispatch;
using Domain.Models.Routing;
using Domain.Rules;
using Domain.Rules.Routing;
using Microsoft.Extensions.Logging;

namespace Application.Features.Routing.Services.Routes;

public sealed partial class RouteChoiceService(
  IAppDbContext db,
  RoutePlanningService planning,
  IRoutingProvider routing,
  IRouteAlternativesProvider alternatives,
  RouteChoiceDrafts drafts,
  TimeProvider clock,
  ReadCache reads,
  RoutePreparationQueue preparation,
  ILogger<RouteChoiceService> logger,
  TruckPlanningInputsReader inputs,
  RoutePlanStore plans,
  PlanningWorkPublication publication,
  TruckPlanningProfileService profiles
)
{
  public async Task<RouteChoicePreview> PreviewAsync(
    Guid dispatch,
    Guid owner,
    RouteChoiceRequest request,
    CancellationToken ct
  )
  {
    var (work, executionLegId) = await planning.CaptureWorkAsync(
      dispatch,
      ct,
      request.ExecutionLegId
    );
    var load = PlanningWorkPolicy.Resolve(work, dispatch, executionLegId);
    if (load.Status is "completed" or "cancelled" or "canceled")
      throw new RoutePlanningException("This load is no longer active.");
    if (load.Stops.Length > 0 && load.Stops.All(s => s.IsCompleted))
      throw new RoutePlanningException("This load has no remaining stops.");
    var profile = await planning.ProfileAsync(load.TruckId!.Value, ct);
    await profiles.RequireRoutingCurrentAsync(load, profile, ct);
    if (await PrepareCurrentRoadAsync(work, load, profile, ct))
    {
      (work, executionLegId) = await planning.CaptureWorkAsync(
        dispatch,
        ct,
        executionLegId
      );
      load = PlanningWorkPolicy.Resolve(work, dispatch, executionLegId);
      profile = await planning.ProfileAsync(load.TruckId!.Value, ct);
      await profiles.RequireRoutingCurrentAsync(load, profile, ct);
    }
    List<PlanStop> stops = [];
    var locations = await StopLocation.ResolveAsync(
      load.Id,
      load.ExecutionLegId,
      load.Stops,
      db,
      routing,
      ct
    );
    foreach (var stop in load.Stops.OrderBy(s => s.Sequence))
      stops.Add(
        new(
          stop.Id,
          stop.Name,
          StopLocation.Address(stop),
          stop.Sequence,
          locations[stop.Id]
        )
        {
          Job = stop.Job,
          StateAfter = stop.StateAfter,
        }
      );
    var current = await CurrentAsync(work, load, profile, stops, ct);
    if (current is not null)
      stops = current.Stops;
    var via = request.ViaPoints;
    if (request.UseSavedVia && load.RouteChoiceRevision > 0)
    {
      var choice = await db
        .DispatchRouteChoices.AsNoTracking()
        .SingleOrDefaultAsync(
          x =>
            x.DispatchId == dispatch && x.ExecutionLegId == load.ExecutionLegId,
          ct
        );
      if (choice is not null)
        via =
          JsonSerializer
            .Deserialize<SavedRouteChoice>(
              choice.ChoiceJson,
              RoutingJson.Options
            )
            ?.ViaPoints ?? [];
    }
    if (current is not null && request.UseSavedVia)
      via = via.Where(v => stops.Skip(1).Any(s => s.Id == v.BeforeStopId))
        .Where(v => !PassedVia(v, current))
        .ToList();
    var expanded = RouteViaGeometry.Expand(stops, via);
    var routes = request.Alternatives
      ? await alternatives.CalculateAlternativesAsync(
        expanded.Points,
        profile,
        ct
      )
      : [await routing.CalculateAsync(expanded.Points, profile, ct)];
    if (
      routes.Count is < 1 or > 3
      || routes.Any(route => !RouteAnchoring.Matches(route, expanded.Points))
    )
      throw new RoutePlanningException(
        "No complete truck route was returned for these locations."
      );
    var saved = await db
      .DispatchBaseRoutes.AsNoTracking()
      .SingleOrDefaultAsync(
        x =>
          x.DispatchId == dispatch && x.ExecutionLegId == load.ExecutionLegId,
        ct
      );
    var baseline =
      current is not null ? current.Baseline
      : saved is not null
      && saved.InputHash == BaseRouteService.Signature(load, profile)
        ? SavedRouteReader.Route(saved.RouteJson, stops.Count - 1)
      : null;
    var collapsed = routes
      .Select(route => RouteViaGeometry.Collapse(route, expanded.StopIndexes))
      .DistinctBy(route =>
        JsonSerializer.Serialize(
          route.Legs.Select(leg => leg.Points),
          RoutingJson.Options
        )
      )
      .ToList();
    foreach (var route in collapsed)
      if (!RouteAnchoring.Matches(route, stops.Select(s => s.Point).ToList()))
        throw new RoutePlanningException(
          "A route option does not reach every stop."
        );
    var reference = baseline ?? collapsed[0];
    var preview = new RouteChoicePreview(
      Guid.NewGuid(),
      dispatch,
      load.TruckId.Value,
      load.LoadNumber,
      load.RouteChoiceRevision,
      clock.GetUtcNow().UtcDateTime.AddMinutes(10),
      stops,
      via,
      collapsed
        .Select(
          (route, index) =>
            new RouteChoiceOption(
              index + 1,
              route,
              route.Miles - reference.Miles,
              route.Seconds - reference.Seconds
            )
        )
        .ToList(),
      baseline
    )
    {
      OriginUpdatedAt = current?.UpdatedAt,
      ExecutionLegId = load.ExecutionLegId,
    };
    await using var transaction = await publication.BeginAsync(work, ct);
    await profiles.RequireRoutingCurrentAsync(load, profile, ct);
    await drafts.StoreAsync(
      new(
        owner,
        RoutePlanInputs.Hash(load, profile),
        JsonSerializer.Serialize(profile, RoutingJson.Options),
        preview
      )
      {
        Current = current?.Context,
        Work = new(work.AsOf, work.InputSignature),
      },
      ct
    );
    await transaction.CommitAsync(ct);
    return preview;
  }

  public async Task<long> SaveAsync(
    Guid dispatch,
    Guid owner,
    RouteChoiceSave request,
    CancellationToken ct
  )
  {
    var draft = await drafts.GetAsync(
      request.PreviewId,
      owner,
      dispatch,
      ct,
      request.ExecutionLegId
    );
    var preview = draft.Preview;
    if (
      request.Option < 1
      || request.Option > preview.Options.Count
      || request.Revision != preview.Revision
    )
      throw new RoutePlanningException(
        "This route option changed. Calculate the preview again."
      );
    await ProcessGates.Dispatch.WaitAsync(ct);
    try
    {
      if (draft.Work is null)
        throw new RoutePlanningException(
          "This preview needs refreshing. Calculate the route options again."
        );
      var captured = await inputs.ReadFreshAsync(
        preview.TruckId,
        ct,
        asOf: draft.Work.AsOf
      );
      if (captured?.Itinerary.InputSignature != draft.Work.InputSignature)
        throw new RoutePlanningException(
          "The truck work changed. Calculate the preview again."
        );
      var work = captured.Itinerary;
      var load = PlanningWorkPolicy.Resolve(
        work,
        dispatch,
        preview.ExecutionLegId
      );
      var source = await db.Dispatches.SingleAsync(x => x.Id == dispatch, ct);
      var profile = await planning.ProfileAsync(load.TruckId!.Value, ct);
      await profiles.RequireRoutingCurrentAsync(load, profile, ct);
      if (
        !load.ExecutionLegId.HasValue
          && source.RouteChoiceRevision != load.RouteChoiceRevision
        || load.RouteChoiceRevision != preview.Revision
        || draft.Inputs != RoutePlanInputs.Hash(load, profile)
      )
        throw new RoutePlanningException(
          "The load or truck settings changed. Calculate the preview again."
        );
      var current = draft.Current is null
        ? null
        : await ValidateCurrentAsync(work, load, profile, draft, ct);
      if (
        draft.Current is null
        && (load.Status == "in_transit" || load.Stops.Any(s => s.IsCompleted))
        && await PlanningCurrency.IsCurrentAsync(work, load, plans, profile, ct)
      )
        throw new RoutePlanningException(
          "This load has started. Calculate its remaining route again."
        );
      var displayReference = draft.Current is { } display
        ? await RouteDisplayReference.ReconnectAsync(
          display.FullRoute,
          display.FullStops,
          preview.Stops.Skip(1).ToList(),
          preview.Options[request.Option - 1].Route,
          profile,
          routing,
          ct
        )
        : null;
      await using var transaction = await publication.BeginAsync(work, ct);
      await profiles.RequireRoutingCurrentAsync(load, profile, ct);
      if (load.ExecutionLegId is { } legId)
      {
        if (!await db.LockExecutionLegAsync(legId, load.AssignmentRevision, ct))
          throw new RoutePlanningException("The route assignment changed.");
        var leg = await db.ExecutionLegs.SingleAsync(x => x.Id == legId, ct);
        if (leg.RouteChoiceRevision != preview.Revision)
          throw new RoutePlanningException("The selected route changed.");
        leg.RouteChoiceRevision = checked(leg.RouteChoiceRevision + 1);
        load = load with { RouteChoiceRevision = leg.RouteChoiceRevision };
      }
      else
      {
        source.RouteChoiceRevision = checked(source.RouteChoiceRevision + 1);
        load = load with { RouteChoiceRevision = source.RouteChoiceRevision };
      }
      var saved = await db.DispatchRouteChoices.SingleOrDefaultAsync(
        x =>
          x.DispatchId == dispatch && x.ExecutionLegId == load.ExecutionLegId,
        ct
      );
      if (saved is null)
      {
        saved = new()
        {
          Id = Guid.NewGuid(),
          DispatchId = dispatch,
          ExecutionLegId = load.ExecutionLegId,
        };
        db.DispatchRouteChoices.Add(saved);
      }
      var selected = preview.Options[request.Option - 1].Route;
      saved.InputHash = BaseRouteService.ChoiceInputs(load, profile);
      saved.ChoiceJson = RoutePlanStorage.Serialize(
        draft.Current is { } context
          ? new SavedRouteChoice(
            context.FullStops,
            preview.ViaPoints,
            context.FullRoute
          )
          {
            Remaining = new(preview.Stops, selected),
          }
          : new SavedRouteChoice(preview.Stops, preview.ViaPoints, selected)
      );
      saved.Revision = load.RouteChoiceRevision;
      saved.RecordedAt = clock.GetUtcNow().UtcDateTime;
      saved.RecordedBy = owner;
      var baseline = await db.DispatchBaseRoutes.SingleOrDefaultAsync(
        x =>
          x.DispatchId == dispatch && x.ExecutionLegId == load.ExecutionLegId,
        ct
      );
      if (baseline is null)
      {
        baseline = new()
        {
          Id = Guid.NewGuid(),
          DispatchId = dispatch,
          ExecutionLegId = load.ExecutionLegId,
        };
        db.DispatchBaseRoutes.Add(baseline);
      }
      baseline.InputHash = BaseRouteService.Signature(load, profile);
      var fullRoute = draft.Current?.FullRoute ?? selected;
      baseline.RouteJson = RoutePlanStorage.Serialize(fullRoute);
      baseline.CalculatedAt = fullRoute.CalculatedAt;
      if (current is not null)
      {
        var plan = current.Plan;
        plan.Version = checked(plan.Version + 1);
        plan.FromCurrentPosition = true;
        plan.Stops = preview.Stops.Skip(1).ToList();
        plan.Route = selected;
        plan.ReferenceStops = draft.Current!.FullStops;
        plan.ReferenceRoute = displayReference;
        plan.CalculatedAt = selected.CalculatedAt;
        plan.LastReroutedAt = clock.GetUtcNow().UtcDateTime;
        plan.LastReroutePosition = preview.Stops[0].Point;
        plan.FuelPlan = null;
        plan.FuelRecommendations = null;
        plan.Tracking.OffRouteSince = null;
        await plans.SaveBuiltAsync(
          current.Entity,
          plan,
          RoutePlanInputs.Hash(load, profile),
          ct
        );
      }
      ExecutionPlanningChanges.RouteSaved(
        db,
        load.Id,
        load.ExecutionLegId,
        load.TruckId,
        load.AssignmentRevision,
        clock.GetUtcNow().UtcDateTime
      );
      await db.SaveChangesAsync(ct);
      await publication.CommitAsync(
        transaction,
        load.TruckId!.Value,
        ct,
        () => InvalidateSavedRoute(dispatch, load.ExecutionLegId)
      );
      preparation.MarkDirty(dispatch);
      preparation.MarkTruckDirty(load.TruckId.Value);
      logger.LogInformation(
        "Route choice saved for {DispatchId} by {ActorId}, revision {Revision}",
        dispatch,
        owner,
        saved.Revision
      );
      return saved.Revision;
    }
    finally
    {
      ProcessGates.Dispatch.Release();
    }
  }
}
