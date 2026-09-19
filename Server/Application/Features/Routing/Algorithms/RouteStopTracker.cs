using Application.Features.Fleet.Models;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services.Routes;
using DispatchEntity = global::Domain.Entities.Dispatch.Dispatch;

namespace Application.Features.Routing.Algorithms;

public static class RouteStopTracker
{
  public static void Update(
    RoutePlan plan,
    DispatchEntity load,
    TruckLocation? truck,
    DateTime now
  ) => Update(plan, RouteWorkProjection.Capture(load), truck, now);

  public static void Update(
    RoutePlan plan,
    RouteWorkSnapshot load,
    TruckLocation? truck,
    DateTime now
  )
  {
    var stops = plan.ReferenceStops ?? plan.Stops;
    var tracking = plan.Tracking;
    var passed = tracking
      .PassedStopIds.Where(tracking.VisitedStops.ContainsKey)
      .ToHashSet();
    var imported = load.Stops.ToDictionary(x => x.Id);
    var lastReported = stops.FindLastIndex(x =>
      imported.TryGetValue(x.Id, out var stop)
      && (
        stop.DepartedAt.HasValue
        || stop.PickedUpAt.HasValue
        || stop.DeliveredAt.HasValue
      )
    );
    foreach (var stop in stops.Take(lastReported + 1))
      passed.Add(stop.Id);
    var handoff = stops.FindIndex(x =>
      imported.TryGetValue(x.Id, out var source) && source.AwaitingHandoff
    );
    if (handoff >= 0)
      passed.ExceptWith(stops.Skip(handoff).Select(x => x.Id));
    passed.ExceptWith(
      load.Stops.Where(s => s.ManualCompletionRevision > 0 && !s.IsCompleted)
        .Select(s => s.Id)
    );
    var fresh = !TruckLocationFreshness.IsStale(truck, now);
    var position = truck is null
      ? null
      : new RoutePoint((double)truck.Latitude, (double)truck.Longitude);
    fresh &= position?.IsValid == true;
    var earlierPending = false;
    for (var i = 0; i < stops.Count; i++)
    {
      var stop = stops[i];
      if (!imported.TryGetValue(stop.Id, out var source))
        continue;
      if (source.AwaitingHandoff)
      {
        earlierPending = true;
        continue;
      }
      if (source.IsCompleted)
        passed.Add(stop.Id);
      if (passed.Contains(stop.Id))
        continue;
      // An explicit undo wins over inferred GPS departure, never over provider
      // facts.
      if (source.ManualCompletionRevision > 0 && !source.IsCompleted)
      {
        earlierPending = true;
        continue;
      }
      var due =
        source.ScheduledDate is null
        || source.ScheduledDate <= DateOnly.FromDateTime(now);
      var complete = false;
      if (fresh && due && !earlierPending)
      {
        var distance = RouteGeometry.Distance(position!, stop.Point);
        if (distance <= .5 && !tracking.VisitedStops.ContainsKey(stop.Id))
          tracking.VisitedStops[stop.Id] = truck!.UpdatedAt;
        var leftVisited =
          tracking.VisitedStops.TryGetValue(stop.Id, out var visited)
          && truck!.UpdatedAt > visited.AddSeconds(30)
          && distance > 1;
        complete = leftVisited;
      }
      if (complete)
        passed.Add(stop.Id);
      else
        earlierPending = true;
    }
    tracking.PassedStopIds = stops
      .Where(x => passed.Contains(x.Id))
      .Select(x => x.Id)
      .ToList();
    var nextStop = stops.FirstOrDefault(x => !passed.Contains(x.Id));
    tracking.NextStopId = nextStop?.Id;
    tracking.AllStopsPassed = stops.Count > 0 && nextStop is null;
    tracking.NextStopLabel =
      nextStop is not null
      && imported.TryGetValue(nextStop.Id, out var nextSource)
        ? $"{(nextSource.Job.Equals("Drop Off", StringComparison.OrdinalIgnoreCase) ? "Delivery" : nextSource.Job.Equals("Pick Up", StringComparison.OrdinalIgnoreCase) ? "Pickup" : "Stop")} · {string.Join(", ", new[] { nextSource.City, nextSource.Province }.Where(x => !string.IsNullOrWhiteSpace(x)))}".Trim(
          ' ',
          '·'
        )
        : "";
  }
}
