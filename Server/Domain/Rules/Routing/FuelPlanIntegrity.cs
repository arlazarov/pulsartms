using Domain.Models.Routing;

namespace Domain.Rules.Routing;

// What makes a saved fuel plan coherent enough to keep. A plan is only
// worth storing if it still describes one truck's run: the stops in the
// order they are driven, the purchases sitting between the stops they were
// chosen for, the saved roads belonging to the loads they were built from,
// and every mile a finite number that never goes backwards. Nothing here
// asks how the plan is stored - a plan that fails these questions is wrong
// wherever it is written.
public static class FuelPlanIntegrity
{
  public const int MaximumItineraryStops = 40;
  public const int MaximumRoutePoints = 200_000;

  // Asked in two parts because the plan arrives in two parts: the summary
  // is read back on its own, and the roads are attached afterwards.
  public static bool SummaryHolds(TruckFuelPlanSnapshot? value)
  {
    if (
      value is null
      || value.TruckId == Guid.Empty
      || value.RootDispatchId == Guid.Empty
      || value.CalculatedAt.Kind != DateTimeKind.Utc
      || value.CalculatedAt == default
      || value.Plan is not { } plan
      || plan.TruckId != value.TruckId
      || plan.CalculatedAt != value.CalculatedAt
      || plan.ExecutionLegId != value.RootExecutionLegId
      || plan.AssignmentRevision != value.AssignmentRevision
      || value.RootExecutionLegId == Guid.Empty
      || value.AssignmentRevision < 0
      || !value.RootExecutionLegId.HasValue && value.AssignmentRevision != 0
      || plan.DispatchIds
        is not { Count: >= 1 and <= MaximumItineraryStops } dispatches
      || dispatches[0] != value.RootDispatchId
      || dispatches.Any(x => x == Guid.Empty)
      || dispatches.Distinct().Count() != dispatches.Count
      || value.Stops is not { Count: >= 1 and <= MaximumItineraryStops } stops
      || plan.Stops is not { Count: <= 50 } purchases
      || plan.Notes is null
      || plan.RefreshReasons is null
      || plan.RouteChecks is null
      || plan.DispatchSignatures is null
      || !double.IsFinite(plan.RemainingMiles)
      || plan.RemainingMiles < 0
      || !double.IsFinite(plan.StartAccessMiles)
      || plan.StartAccessMiles < 0
    )
      return false;
    if (
      value.RootExecutionLegId.HasValue
      && dispatches.Count > 1
      && dispatches.Any(id =>
        !plan.DispatchSignatures.TryGetValue(id, out var signature)
        || string.IsNullOrWhiteSpace(signature)
      )
    )
      return false;
    double previous = 0;
    var seen = new HashSet<Guid>();
    var owners = new List<Guid>();
    var intervals =
      new Dictionary<Guid, (Guid DispatchId, double Start, double End)>();
    // A stop carries the assignment of the load it belongs to. The root
    // carries this plan's; a load chained after it carries its own, which is
    // an execution leg where that load has already been accepted and nothing
    // where it has not - and every stop of one load says the same thing.
    // Read as "nothing after the root", this refused to keep a plan over a
    // truck's own accepted work, which is how the loads ahead of it are held
    // now: 11006 stood at its delivery with three of them and two thousand
    // miles to drive, and no fuel plan could be saved for any of it.
    var assignments = new Dictionary<Guid, (Guid? Leg, long Revision)>
    {
      [value.RootDispatchId] = (
        value.RootExecutionLegId,
        value.AssignmentRevision
      ),
    };
    foreach (var item in stops)
    {
      if (
        item?.Stop is not { } stop
        || !dispatches.Contains(item.DispatchId)
        || !assignments.TryAdd(
          item.DispatchId,
          (item.ExecutionLegId, item.AssignmentRevision)
        )
          && assignments[item.DispatchId]
            != (item.ExecutionLegId, item.AssignmentRevision)
        || !item.ExecutionLegId.HasValue && item.AssignmentRevision != 0
        || item.AssignmentRevision < 0
        || stop.Id == Guid.Empty
        || !seen.Add(stop.Id)
        || stop.Point?.IsValid != true
        || !double.IsFinite(item.EndMiles)
        || item.EndMiles < previous
      )
        return false;
      if (owners.Count == 0 || owners[^1] != item.DispatchId)
        owners.Add(item.DispatchId);
      intervals.Add(stop.Id, (item.DispatchId, previous, item.EndMiles));
      previous = item.EndMiles;
    }
    if (!owners.SequenceEqual(dispatches))
      return false;
    if (
      value.HistoryDependencies is { } history
      && (
        history.Version < 1
        || history.Batches.IsDefault
        || history.Batches.Length > 128
        || history.Batches.Any(batch =>
          batch is null
          || batch.Inputs.IsDefaultOrEmpty
          || batch.Inputs.Length > 40
          || batch.Inputs.Any(input =>
            input is null
            || input.InputSignature is not { Length: 64 } signature
            || !signature.All(Uri.IsHexDigit)
            || input.Current is not { } current
            || current.Id == Guid.Empty
            || current.TruckId != value.TruckId
            || current.ExecutionLegId == Guid.Empty
            || !dispatches.Contains(current.Id)
              && current.Id != plan.ArrivalPolicy?.NextDispatchId
            || current.Stops.IsDefaultOrEmpty
            || current.Stops.Length > 49
            || current.Stops.Any(stop => stop is null || stop.Id == Guid.Empty)
            || current.Stops.Select(stop => stop.Id).Distinct().Count()
              != current.Stops.Length
          )
          || batch.Inputs.Select(input => input.Current.Id).Distinct().Count()
            != batch.Inputs.Length
        )
      )
    )
      return false;
    if (
      value.RoadDependencies is { } dependencies
      && (
        dependencies.Version < 1
        || dependencies.Roads.IsDefaultOrEmpty
        || dependencies.Roads.Length > 128
        || dependencies.Roads.Any(road =>
          road is null
          || road.Work is null
          || road.Work.DispatchId == Guid.Empty
          || road.Work.ExecutionLegId == Guid.Empty
          || !Enum.IsDefined(road.Kind)
          || road.ProgressSignature is not null
          || road.Signature is not { Length: 64 } signature
          || !signature.All(Uri.IsHexDigit)
          // A saved road belongs to a load, and carries that load's
          // assignment - the root's for the root, its own for a load
          // chained after it, none for a load not yet accepted.
          || road.Work.ExecutionLegId
            != (
              assignments.TryGetValue(road.Work.DispatchId, out var owner)
                ? owner.Leg
                : null
            )
          || !dispatches.Contains(road.Work.DispatchId)
            && road.Work.DispatchId != plan.ArrivalPolicy?.NextDispatchId
        )
        || !dependencies.Roads.Any(road =>
          road.Kind == SavedRoadKind.Plan
          && road.Work.DispatchId == value.RootDispatchId
          && road.Work.ExecutionLegId == value.RootExecutionLegId
        )
      )
    )
      return false;
    if (
      plan.ArrivalPolicy?.NextDispatchId is { } next
      && (
        stops[^1].ExecutionLegId.HasValue
        || next == Guid.Empty
        || dispatches.Contains(next)
        || !plan.DispatchSignatures.TryGetValue(next, out var signature)
        || string.IsNullOrWhiteSpace(signature)
      )
    )
      return false;
    var visits = new HashSet<string>(StringComparer.Ordinal);
    double purchaseMiles = -1;
    foreach (var purchase in purchases)
    {
      var routeMiles = plan.EstimatedStationAccess
        ? purchase?.RouteMilesAhead
        : purchase?.MilesAhead;
      if (
        purchase is null
        || purchase.StationId == Guid.Empty
        || purchase.Point?.IsValid != true
        || string.IsNullOrWhiteSpace(purchase.VisitKey)
        || !visits.Add(purchase.VisitKey)
        || !intervals.TryGetValue(purchase.BeforeStopId, out var interval)
        || interval.DispatchId != purchase.DispatchId
        || !double.IsFinite(purchase.MilesAhead)
        || purchase.MilesAhead < 0
        || routeMiles is not { } mile
        || !double.IsFinite(mile)
        || mile < 0
        || mile < purchaseMiles
        || mile < interval.Start - .01
        || mile > interval.End + .01
        || plan.EstimatedStationAccess
          && (
            !double.IsFinite(purchase.DetourMiles)
            || purchase.DetourMiles < 0
            || !double.IsFinite(purchase.DetourMinutes)
            || purchase.DetourMinutes < 0
          )
        || !double.IsFinite(purchase.BuyGallons)
        || purchase.BuyGallons < 0
      )
        return false;
      purchaseMiles = mile;
    }
    return true;
  }

  public static bool RoutesHold(TruckFuelPlanSnapshot value)
  {
    if (value.Plan.EstimatedStationAccess)
      return value.CheckedRoute is null
        && (
          value.BaselineRoute is null
          || ValidRoute(value.BaselineRoute, value.Stops, true)
        );
    if (
      value.CheckedRoute is { } route && !ValidRoute(route, value.Stops, true)
      || value.BaselineRoute is { } baseline
        && !ValidRoute(baseline, value.Stops, false)
    )
      return false;
    if (
      value.CheckedRoute is not { } checkedRoute
      || value.BaselineRoute is not { } baselineRoute
    )
      return true;
    return SameEndpoint(
        checkedRoute.Legs[0].Points[0],
        baselineRoute.Legs[0].Points[0]
      )
      && checkedRoute
        .Legs.Zip(baselineRoute.Legs)
        .All(pair =>
          SameEndpoint(pair.First.Points[^1], pair.Second.Points[^1])
        );
  }

  private static bool ValidRoute(
    TruckRoute route,
    IReadOnlyList<FuelItineraryStop> stops,
    bool compareEndMiles
  )
  {
    if (
      !double.IsFinite(route.Miles)
      || route.Miles <= 0
      || !double.IsFinite(route.Seconds)
      || route.Seconds < 0
      || route.Legs is not { Count: >= 1 and <= MaximumItineraryStops } legs
      || legs.Count != stops.Count
      || route.Warnings is null
      || route.Points is null
      || route.Points.Count > MaximumRoutePoints
    )
      return false;
    long points = 0;
    double miles = 0;
    double seconds = 0;
    for (var i = 0; i < legs.Count; i++)
    {
      var leg = legs[i];
      if (
        leg is null
        || leg.Points is not { Count: >= 2 } coordinates
        || !double.IsFinite(leg.Miles)
        || leg.Miles < 0
        || !double.IsFinite(leg.Seconds)
        || leg.Seconds < 0
      )
        return false;
      points += coordinates.Count;
      if (
        points > MaximumRoutePoints
        || coordinates.Any(x => x?.IsValid != true)
      )
        return false;
      if (
        !SameEndpoint(coordinates[^1], stops[i].Stop.Point, .5)
        || i > 0 && !SameEndpoint(legs[i - 1].Points[^1], coordinates[0])
      )
        return false;
      miles += leg.Miles;
      seconds += leg.Seconds;
      if (compareEndMiles && Math.Abs(stops[i].EndMiles - miles) > .01)
        return false;
    }
    return Math.Abs(route.Miles - miles) <= .01
      && Math.Abs(route.Seconds - seconds) <= .01;
  }

  private static bool SameEndpoint(
    RoutePoint first,
    RoutePoint second,
    double toleranceMiles = .05
  )
  {
    // Provider road snapping can slightly change the coordinate of the same
    // mandatory stop.
    var latitudeMiles = (first.Latitude - second.Latitude) * 69;
    var longitudeMiles =
      (first.Longitude - second.Longitude)
      * 69
      * Math.Cos((first.Latitude + second.Latitude) * Math.PI / 360);
    return latitudeMiles * latitudeMiles + longitudeMiles * longitudeMiles
      <= toleranceMiles * toleranceMiles;
  }
}
