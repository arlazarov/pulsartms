using System.Text.Json;
using Domain.Entities.Execution;
using Domain.Models.Routing;
using Domain.Rules;

namespace Domain.Rules.Routing;

public static partial class FuelPlanProjection
{
  private const int MinimumEstimatedAccessVersion = 28;

  public static bool SameScope(TruckFuelPlanSnapshot saved, RoutePlan plan)
  {
    if (
      saved.TruckId != plan.TruckId
      || saved.Plan.ExecutionLegId != saved.RootExecutionLegId
      || saved.Plan.AssignmentRevision != saved.AssignmentRevision
    )
      return false;
    if (saved.RootDispatchId == plan.DispatchId)
      return saved.RootExecutionLegId == plan.ExecutionLegId
        && saved.AssignmentRevision == plan.AssignmentRevision;
    var scope = saved
      .Stops.Where(x => x.DispatchId == plan.DispatchId)
      .ToArray();
    return scope.Length > 0
      && scope.All(x =>
        x.ExecutionLegId == plan.ExecutionLegId
        && x.AssignmentRevision == plan.AssignmentRevision
      );
  }

  public static bool RemainingStopsMatch(
    IReadOnlyList<FuelItineraryStop> itinerary,
    Guid currentDispatchId,
    Guid? nextStopId,
    IReadOnlyList<IWorkFacts> loads
  )
  {
    var index = itinerary
      .ToList()
      .FindIndex(x =>
        x.DispatchId == currentDispatchId && x.Stop.Id == nextStopId
      );
    if (index < 0)
      return false;
    var scope = itinerary[index];
    var current = loads
      .Where(x =>
        x.Id == currentDispatchId
        && x.ExecutionLegId == scope.ExecutionLegId
        && x.AssignmentRevision == scope.AssignmentRevision
      )
      .ToArray();
    if (current.Length != 1)
      return false;
    if (
      !TrySelectLoads(
        new()
        {
          TruckId = current[0].TruckId ?? Guid.Empty,
          DispatchId = currentDispatchId,
          ExecutionLegId = scope.ExecutionLegId,
          AssignmentRevision = scope.AssignmentRevision,
        },
        loads,
        out var selected
      )
    )
      return false;
    var byLoad = selected.ToDictionary(x => x.Id);
    foreach (var group in itinerary.Skip(index).GroupBy(x => x.DispatchId))
    {
      if (
        !byLoad.TryGetValue(group.Key, out var load)
        || group.Any(x =>
          x.ExecutionLegId != load.ExecutionLegId
          || x.AssignmentRevision != load.AssignmentRevision
        )
      )
        return false;
      var remaining = load
        .Stops.Where(x => !x.DriverOnly)
        .OrderBy(x => x.Sequence)
        .AsEnumerable();
      // Current-route progress may have passed a prefix before actual events
      // arrive.
      if (group.Key == currentDispatchId)
        remaining = remaining.SkipWhile(x => x.Id != nextStopId);
      if (
        !remaining
          .Where(x => !x.IsCompleted)
          .Select(x => x.Id)
          .SequenceEqual(group.Select(x => x.Stop.Id))
      )
        return false;
    }
    return true;
  }

  public static bool AssignmentsMatch(
    FuelPlan fuel,
    Guid currentDispatchId,
    IReadOnlyList<IWorkFacts> loads
  )
  {
    var root = currentDispatchId == fuel.DispatchIds.FirstOrDefault();
    if (
      !TrySelectLoads(
        new()
        {
          TruckId = fuel.TruckId,
          DispatchId = currentDispatchId,
          ExecutionLegId = root ? fuel.ExecutionLegId : null,
          AssignmentRevision = root ? fuel.AssignmentRevision : 0,
        },
        loads,
        out var selected
      )
    )
      return false;
    loads = selected;
    var index = fuel.DispatchIds.IndexOf(currentDispatchId);
    var current = loads.ToList().FindIndex(x => x.Id == currentDispatchId);
    if (index < 0 || current < 0)
      return false;
    var expected = fuel.DispatchIds.Skip(index).ToArray();
    var actual = loads.Skip(current).Take(expected.Length).ToArray();
    if (!actual.Select(x => x.Id).SequenceEqual(expected))
      return false;
    if (
      actual.Any(load =>
        !fuel.DispatchSignatures.TryGetValue(load.Id, out var signature)
        || signature != FuelWorkSignature.LoadSignature(load)
      )
    )
      return false;
    var next = loads.Skip(current + expected.Length).FirstOrDefault();
    return next?.Id == fuel.ArrivalPolicy?.NextDispatchId
      && (
        next is null
        || fuel.DispatchSignatures.TryGetValue(next.Id, out var nextSignature)
          && nextSignature == FuelWorkSignature.LoadSignature(next)
      );
  }

  private static bool TrySelectLoads(
    RoutePlan plan,
    IReadOnlyList<IWorkFacts> loads,
    out List<IWorkFacts> selected
  )
  {
    try
    {
      selected = FuelHorizonLoads.SelectLoads(plan, loads);
      return true;
    }
    catch (RoutePlanningException)
    {
      selected = [];
      return false;
    }
  }
}
