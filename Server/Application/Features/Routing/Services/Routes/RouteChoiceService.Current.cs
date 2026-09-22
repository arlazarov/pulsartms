using Domain.Entities.Dispatch;
using Domain.Models.Execution;
using Domain.Models.Fleet;
using Domain.Models.Routing;
using Domain.Rules;
using Domain.Rules.Routing;

namespace Application.Features.Routing.Services.Routes;

public sealed partial class RouteChoiceService
{
  private sealed record CurrentPreview(
    List<PlanStop> Stops,
    RouteChoiceCurrentContext Context,
    DateTime UpdatedAt,
    TruckRoute? Baseline,
    RoutePlan Plan
  );

  private sealed record CurrentSave(DispatchRoutePlan Entity, RoutePlan Plan);

  private async Task<bool> PrepareCurrentRoadAsync(
    TruckItinerarySnapshot work,
    RouteWorkSnapshot load,
    TruckRouteProfile profile,
    CancellationToken ct
  )
  {
    if (
      !await PlanningCurrency.IsCurrentAsync(work, load, plans, profile, ct)
      || !Fresh(await planning.LocationAsync(load.TruckId!.Value, ct))
    )
      return false;
    var saved = await plans.ReadAsync(load.Id, ct, load.ExecutionLegId);
    if (
      saved is not null
      && RoutePlanInputs.Matches(saved, load, profile)
      && RoutePlanStorage.Read(saved) is not null
    )
      return false;
    await planning.BuildAsync(
      load.Id,
      new(profile) { ExecutionLegId = load.ExecutionLegId },
      ct,
      automatic: true,
      capturedWork: work
    );
    return true;
  }

  private async Task<CurrentPreview?> CurrentAsync(
    TruckItinerarySnapshot work,
    RouteWorkSnapshot load,
    TruckRouteProfile profile,
    List<PlanStop> stops,
    CancellationToken ct
  )
  {
    if (!await PlanningCurrency.IsCurrentAsync(work, load, plans, profile, ct))
      return null;
    var entity = await plans.ReadAsync(load.Id, ct, load.ExecutionLegId);
    var old = entity is null ? null : RoutePlanStorage.Read(entity);
    var started =
      load.Status == "in_transit"
      || load.Stops.Any(s => s.IsCompleted)
      || old?.FromCurrentPosition == true;
    var truck = await planning.LocationAsync(load.TruckId!.Value, ct);
    if (!Fresh(truck))
    {
      if (started)
        throw new RoutePlanningException(
          "A fresh truck GPS location is needed. Update the location and try again."
        );
      return null;
    }
    if (
      old is null
      || entity!.TruckId != load.TruckId
      || old.TruckId != load.TruckId
      || old.DispatchId != load.Id
      || !RoutePlanInputs.Matches(entity, load, profile)
    )
    {
      if (started)
        throw new RoutePlanningException(
          "The current route needs updating before comparing its remaining road."
        );
      return null;
    }
    var saved = await db
      .DispatchBaseRoutes.AsNoTracking()
      .SingleOrDefaultAsync(
        x => x.DispatchId == load.Id && x.ExecutionLegId == load.ExecutionLegId,
        ct
      );
    var full =
      saved?.InputHash == BaseRouteService.Signature(load, profile)
        ? SavedRouteReader.Route(saved.RouteJson, stops.Count - 1)
        : null;
    full ??=
      old.ReferenceStops?.Select(s => s.Id)
        .SequenceEqual(stops.Select(s => s.Id)) == true
        ? old.ReferenceRoute
      : !old.FromCurrentPosition ? old.Route
      : null;
    if (
      full is null
      || !RouteAnchoring.Matches(full, stops.Select(s => s.Point).ToList())
    )
      throw new RoutePlanningException(
        "The saved full route is unavailable. Update the route before changing its remaining road."
      );
    RouteStopTracker.Update(old, load, truck, clock.GetUtcNow().UtcDateTime);
    var remaining = stops
      .Where(s => !old.Tracking.PassedStopIds.Contains(s.Id))
      .ToList();
    if (remaining.Count == 0)
      throw new RoutePlanningException("This load has no remaining stops.");
    var position = new RoutePoint(
      (double)truck!.Latitude,
      (double)truck.Longitude
    );
    var baseline = RemainingRoad(old, position, remaining);
    var context = new RouteChoiceCurrentContext(
      stops,
      full,
      old.Id,
      old.Version,
      remaining.Select(s => s.Id).ToList()
    );
    return new(
      [
        new(
          Guid.Empty,
          "Current truck location",
          truck.FormattedLocation,
          0,
          position
        )
        {
          Job = "GPS start",
        },
        .. remaining,
      ],
      context,
      truck.UpdatedAt,
      baseline,
      old
    );
  }

  private bool Fresh(TruckLocation? truck) =>
    truck is not null
    && new RoutePoint((double)truck.Latitude, (double)truck.Longitude).IsValid
    && truck.UpdatedAt >= clock.GetUtcNow().UtcDateTime.AddMinutes(-10)
    && truck.UpdatedAt <= clock.GetUtcNow().UtcDateTime.AddMinutes(1);

  private async Task<CurrentSave> ValidateCurrentAsync(
    TruckItinerarySnapshot work,
    RouteWorkSnapshot load,
    TruckRouteProfile profile,
    RouteChoiceDraft draft,
    CancellationToken ct
  )
  {
    var context = draft.Current!;
    if (!await PlanningCurrency.IsCurrentAsync(work, load, plans, profile, ct))
      throw new RoutePlanningException(
        "The current load changed. Calculate the preview again."
      );
    var entity = await plans.ReadForUpdateAsync(
      load.Id,
      ct,
      load.ExecutionLegId
    );
    var plan = entity is null ? null : RoutePlanStorage.Read(entity);
    if (
      plan is null
      || plan.Id != context.PlanId
      || plan.Version != context.PlanVersion
      || entity!.TruckId != load.TruckId
      || !RoutePlanInputs.Matches(entity, load, profile)
    )
      throw new RoutePlanningException(
        "The current route changed. Calculate the preview again."
      );
    var truck = await planning.LocationAsync(load.TruckId!.Value, ct);
    if (
      !Fresh(truck)
      || RouteGeometry.Distance(
        draft.Preview.Stops[0].Point,
        new((double)truck!.Latitude, (double)truck.Longitude)
      ) > 2
    )
      throw new RoutePlanningException(
        "The truck location changed. Calculate the preview again."
      );
    RouteStopTracker.Update(plan, load, truck, clock.GetUtcNow().UtcDateTime);
    if (
      !context
        .FullStops.Where(s => !plan.Tracking.PassedStopIds.Contains(s.Id))
        .Select(s => s.Id)
        .SequenceEqual(context.RemainingStopIds)
    )
      throw new RoutePlanningException(
        "The remaining stops changed. Calculate the preview again."
      );
    return new(entity!, plan);
  }

  private static TruckRoute? RemainingRoad(
    RoutePlan plan,
    RoutePoint position,
    List<PlanStop> remaining
  )
  {
    var indexes = remaining
      .Select(s =>
        plan.Stops.FindIndex(p => p.Id == s.Id)
        + (plan.FromCurrentPosition ? 0 : -1)
      )
      .ToArray();
    if (indexes.Any(i => i < 0 || i >= plan.Route.Legs.Count))
      return null;
    var first = plan.Route.Legs[indexes[0]];
    if (new RouteGeometry(new() { Legs = [first] }).Match(position).Away > .5)
      return null;
    var legs = new List<RouteLeg>
    {
      RouteViaGeometry.Remaining(first, position),
    };
    for (var i = 1; i < indexes.Length; i++)
      legs.Add(
        RouteViaGeometry.JoinLegs(
          plan.Route.Legs.Skip(indexes[i - 1] + 1)
            .Take(indexes[i] - indexes[i - 1])
            .ToList()
        )
      );
    return RouteViaGeometry.Join(
      legs,
      plan.Route.Warnings,
      plan.Route.CalculatedAt
    );
  }

  private static bool PassedVia(RouteViaPoint via, CurrentPreview current)
  {
    if (via.BeforeStopId != current.Stops[1].Id)
      return false;
    var geometry = new RouteGeometry(current.Plan.Route);
    var position = geometry.Match(current.Stops[0].Point);
    var point = geometry.Match(via.Point);
    return position.Away <= .5
      && point.Away <= .1
      && point.Along < position.Along - .05;
  }
}
