using System.Collections.Immutable;
using Application.Features.Routing.Algorithms;

namespace Application.Features.Routing.Models;

public sealed record FuelRoadDependencies(
  int Version,
  ImmutableArray<SavedRoadVersion> Roads
)
{
  public const int CurrentVersion = 1;

  public static FuelRoadDependencies Capture(
    IReadOnlyCollection<SavedRoadVersion> roads
  ) =>
    new(
      CurrentVersion,
      roads.Select(x => x with { ProgressSignature = null }).ToImmutableArray()
    );

  public static IReadOnlyCollection<SavedRoadVersion>? Remaining(
    TruckFuelPlanSnapshot saved,
    RoutePlan current
  )
  {
    if (
      saved.RoadDependencies is not { Version: CurrentVersion } dependencies
      || dependencies.Roads.IsDefaultOrEmpty
      || !FuelPlanProjection.SameScope(saved, current)
    )
      return null;
    var index = saved.Plan.DispatchIds.IndexOf(current.DispatchId);
    if (index < 0)
      return null;
    var remaining = saved.Plan.DispatchIds.Skip(index).ToHashSet();
    if (saved.Plan.ArrivalPolicy?.NextDispatchId is { } next)
      remaining.Add(next);
    return dependencies
      .Roads.Where(x => remaining.Contains(x.Work.DispatchId))
      .ToArray();
  }
}
