using Application.Features.Execution.Interfaces;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services.Deadheads;

namespace Application.Features.Routing.Services.FuelPlanning;

public sealed class FuelSavedInputsValidation(
  ISavedRoadValidation roads,
  DeadheadHistoryService history,
  IExecutionReadScope scope
) : IFuelSavedInputsValidation
{
  public Task<bool> MatchesAsync(
    TruckFuelPlanSnapshot saved,
    RoutePlan current,
    CancellationToken ct
  )
  {
    var remaining = FuelRoadDependencies.Remaining(saved, current);
    if (
      remaining is null
      || saved.HistoryDependencies
        is not { Version: FuelHistoryDependencies.CurrentVersion } dependencies
      || dependencies.Batches.IsDefault
    )
      return Task.FromResult(false);
    var selected = remaining.Select(x => x.Work.DispatchId).ToHashSet();
    var currentStops = saved
      .Stops.Where(x => x.DispatchId == current.DispatchId)
      .ToArray();
    // The incoming connection no longer affects fuel after its first visit.
    if (
      Array.FindIndex(
        currentStops,
        x => x.Stop.Id == current.Tracking.NextStopId
      ) > 0
    )
      selected.Remove(current.DispatchId);
    return MatchesAsync(remaining, dependencies, selected, ct);
  }

  public Task<bool> MatchesAsync(
    FuelRecommendations access,
    CancellationToken ct
  )
  {
    if (
      access.RoadDependencies.Count == 0
      || access.HistoryDependencies
        is not { Version: FuelHistoryDependencies.CurrentVersion } dependencies
      || dependencies.Batches.IsDefault
    )
      return Task.FromResult(false);
    return MatchesAsync(
      access.RoadDependencies,
      dependencies,
      access.RoadDependencies.Select(x => x.Work.DispatchId).ToHashSet(),
      ct
    );
  }

  private Task<bool> MatchesAsync(
    IReadOnlyCollection<SavedRoadVersion> remaining,
    FuelHistoryDependencies dependencies,
    HashSet<Guid> selected,
    CancellationToken ct
  )
  {
    return scope.ReadAsync(
      async token =>
      {
        if (!await roads.MatchesAsync(remaining, token))
          return false;
        foreach (var batch in dependencies.Batches)
        {
          if (!batch.Inputs.Any(x => selected.Contains(x.Current.Id)))
            continue;
          // Replay the complete lookup batch; native predecessor selection
          // can share completed-leg references between its members.
          var fresh = await history.ReadLoadedAsync(
            batch.Inputs.Select(x => x.Current).ToArray(),
            token
          );
          if (
            batch.Inputs.Any(x =>
              selected.Contains(x.Current.Id)
              && fresh.GetValueOrDefault(x.Current.Id)?.InputSignature
                != x.InputSignature
            )
          )
            return false;
        }
        return true;
      },
      ct
    );
  }
}
