using Application.Features.Fuel.Models;
using Application.Features.Fuel.Queries.GetFuelStations;
using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Models;

namespace Application.Features.Routing.Services.FuelPlanning;

// The stops a dispatcher is editing, before any of them is priced: what the
// editor opens on, what it may be given, and what it shows when a choice
// cannot be resolved against the road.
public static class FuelPlanEdits
{
  // The purchases of a saved plan that are still ahead of the truck: what
  // the editor opens on when the dispatcher has not yet changed anything.
  public static List<FuelPlanEditStop> Initial(
    TruckFuelPlanSnapshot? saved,
    RoutePlanningState state,
    FuelHorizonResult horizon
  )
  {
    if (
      saved is null
      || state.Plan is null
      || !FuelPlanProjection.SameScope(saved, state.Plan)
    )
      return [];
    var firstStop = horizon.Itinerary[0].Stop.Id;
    var index = saved
      .Stops.ToList()
      .FindIndex(x =>
        x.Stop.Id == firstStop && x.DispatchId == state.Plan!.DispatchId
      );
    var along = double.NegativeInfinity;
    if (
      saved.Plan.EstimatedStationAccess
      && index >= 0
      && saved.BaselineRoute?.Legs.ElementAtOrDefault(index) is { } leg
      && saved.Plan.DispatchSignatures.GetValueOrDefault(state.Plan!.DispatchId)
        == horizon.DispatchSignatures.GetValueOrDefault(state.Plan.DispatchId)
    )
    {
      var tail = RemainingFuelRoute.TryRead(
        new RoutePlan
        {
          FromCurrentPosition = true,
          Stops = [saved.Stops[index].Stop],
          Route = new() { Legs = [leg] },
        },
        [horizon.Itinerary[0].Stop],
        state.Progress!.Position!,
        FuelAccessEstimate.CurrentPositionToleranceMiles
      );
      var current = horizon.Route.Legs[0];
      // Only verified identical remaining road geometry can silently discard
      // passed purchases.
      if (
        tail?.Legs[0] is { } remainingLeg
        && Math.Abs(remainingLeg.Miles - current.Miles) < .01
        && remainingLeg.Points.Count == current.Points.Count
        && remainingLeg
          .Points.Zip(current.Points)
          .All(x => RouteGeometry.Distance(x.First, x.Second) < .00001)
      )
        along =
          (index == 0 ? 0 : saved.Stops[index - 1].EndMiles)
          + leg.Miles
          - remainingLeg.Miles;
    }
    var remaining = horizon.Itinerary.Select(x => x.Stop.Id).ToHashSet();
    return saved
      .Plan.Stops.Where(x =>
        remaining.Contains(x.BeforeStopId)
        && FuelReservePolicy.PurchaseNotPassed(
          saved.Plan.EstimatedStationAccess
            ? x.RouteMilesAhead ?? x.MilesAhead
            : x.MilesAhead,
          along
        )
      )
      .Select(x => new FuelPlanEditStop(
        x.StationId,
        x.BeforeStopId,
        x.BuyGallons,
        x.FillToTarget
      ))
      .ToList();
  }

  public static void Validate(
    IReadOnlyList<FuelPlanEditStop> edits,
    TruckFuelPlanSnapshot? saved
  )
  {
    if (edits.Count > 40)
      throw new RoutePlanningException(
        "A fuel plan can contain at most 40 stops."
      );
    foreach (var edit in edits)
    {
      if (
        edit is null
        || edit.StationId == Guid.Empty
        || !double.IsFinite(edit.BuyGallons)
        || edit.BuyGallons is < 0 or > 500
      )
        throw new RoutePlanningException(
          "Choose a valid station and fuel quantity."
        );
      if (edit.FillToTarget)
        continue;
      var unchanged =
        saved?.Plan.Stops.Any(x =>
          x.StationId == edit.StationId
          && (
            !edit.BeforeStopId.HasValue || x.BeforeStopId == edit.BeforeStopId
          )
          && !x.FillToTarget
          && Math.Abs(x.BuyGallons - edit.BuyGallons) < 1e-6
        ) == true;
      if (
        !unchanged
        && edit.BuyGallons < FuelOptimizer.MinimumAutomaticPurchaseGallons
      )
        throw new RoutePlanningException(
          "Choose at least 25 US gallons, or select Full tank."
        );
    }
  }

  // A preview of choices that could not be placed on the road - a station
  // across a border, prices that would not load. The dispatcher keeps what
  // was typed and is told why it cannot be calculated, instead of the editor
  // closing on an error.
  public static FuelPlan Unresolved(
    RoutePlan plan,
    FuelHorizonResult horizon,
    IReadOnlyList<FuelPlanEditStop> edits,
    TruckFuelPlanSnapshot? editable,
    IReadOnlyList<FuelStationDto> response,
    bool edited,
    string reason
  )
  {
    var unresolved = new FuelPlan
    {
      TruckId = plan.TruckId,
      ExecutionLegId = plan.ExecutionLegId,
      AssignmentRevision = plan.AssignmentRevision,
      DispatchIds = horizon.DispatchIds,
      ManuallyEdited = edited || editable?.Plan.ManuallyEdited == true,
      NeedsRefresh = true,
      RefreshReasons = [reason],
    };
    unresolved.Stops = edits
      .Select(
        (edit, index) =>
        {
          var previous = editable?.Plan.Stops.FirstOrDefault(x =>
            x.StationId == edit.StationId
            && (
              !edit.BeforeStopId.HasValue || x.BeforeStopId == edit.BeforeStopId
            )
          );
          var station = response.FirstOrDefault(x => x.Id == edit.StationId);
          return new FuelPlanStop
          {
            Number = index + 1,
            StationId = edit.StationId,
            BeforeStopId = edit.BeforeStopId ?? Guid.Empty,
            DispatchId =
              horizon
                .Itinerary.FirstOrDefault(x => x.Stop.Id == edit.BeforeStopId)
                ?.DispatchId
              ?? previous?.DispatchId
              ?? Guid.Empty,
            VisitKey = previous?.VisitKey ?? "",
            Point = previous?.Point ?? new(0, 0),
            Name = station?.Name ?? previous?.Name ?? "Unavailable station",
            Address = station?.Address ?? previous?.Address ?? "",
            BuyGallons = edit.BuyGallons,
            FillToTarget = edit.FillToTarget,
          };
        }
      )
      .ToList();
    return unresolved;
  }
}
