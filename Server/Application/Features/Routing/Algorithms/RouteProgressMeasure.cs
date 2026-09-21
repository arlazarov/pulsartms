using Application.Features.Execution.Models;
using Application.Features.Fleet.Models;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services.Routes;
using DispatchEntity = global::Domain.Entities.Dispatch.Dispatch;

namespace Application.Features.Routing.Algorithms;

// How far along its route a truck is: the miles covered, the miles and the
// driving time left, and how far off the road it stands. A position that is
// stale, or a plan whose inputs have changed, still says where the truck is
// but claims nothing about progress.
public static class RouteProgressMeasure
{
  public static RouteProgress Of(
    RoutePlan plan,
    TruckLocation? truck,
    DispatchEntity load,
    RouteGeometry? exactGeometry = null
  ) => Of(plan, truck, RouteWorkProjection.Capture(load), exactGeometry);

  public static RouteProgress Of(
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
}
