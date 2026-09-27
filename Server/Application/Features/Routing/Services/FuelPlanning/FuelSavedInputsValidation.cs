using System.Security.Cryptography;
using System.Text.Json;
using Application.Caching;
using Application.Features.Execution.Interfaces;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Services.Deadheads;
using Domain.Models.Routing;

namespace Application.Features.Routing.Services.FuelPlanning;

public sealed class FuelSavedInputsValidation(
  ISavedRoadValidation roads,
  DeadheadHistoryService history,
  IExecutionReadScope scope,
  ReadCache reads
) : IFuelSavedInputsValidation
{
  // The item family a committed saved connection bumps for its truck
  // (DeadheadHistoryPublication); route, base road and execution commits
  // bump the truck's planning inputs.
  public const string SavedInputsFamily = "fuel-saved-inputs";

  // One planning refresh checked the same saved plan against the same
  // roads and history up to four times in a row - its preparation, the
  // summary publisher, the check before the price refresh and the price
  // refresh itself - 6 statements each on SQLite (FuelCallerCostTests).
  // An operation that is one unit shares the answer for the same inputs
  // of the check - the remaining roads, the history batches it replays
  // with their signatures, and the selected loads - as long as the
  // generations read before the check are unchanged: a commit here, or
  // one relayed from another process, drops it. A write another process
  // has not yet announced is seen by the next operation, not this one.
  // One share per scope: an operation inside another would end the outer
  // one's early, so it is refused.
  private Dictionary<string, Shared>? shared;

  private sealed record Shared(long Inputs, long Connections, bool Matches);

  public IDisposable Share()
  {
    if (shared is not null)
      throw new InvalidOperationException(
        "The saved fuel inputs check is already shared in this scope."
      );
    shared = [];
    return new Ending(this, shared);
  }

  private sealed class Ending(
    FuelSavedInputsValidation owner,
    Dictionary<string, Shared> share
  ) : IDisposable
  {
    public void Dispose()
    {
      if (ReferenceEquals(owner.shared, share))
        owner.shared = null;
    }
  }

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
    if (shared is null)
      return MatchesAsync(remaining, dependencies, selected, ct);
    return SharedAsync(saved, remaining, dependencies, selected, ct);
  }

  private async Task<bool> SharedAsync(
    TruckFuelPlanSnapshot saved,
    IReadOnlyCollection<SavedRoadVersion> remaining,
    FuelHistoryDependencies dependencies,
    HashSet<Guid> selected,
    CancellationToken ct
  )
  {
    var key = Convert.ToHexString(
      SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(
          new
          {
            saved.TruckId,
            remaining,
            History = dependencies.Batches.Where(batch =>
              Replayed(batch, selected)
            ),
            Selected = selected.Order(),
          }
        )
      )
    );
    // Read before the check, so a commit during it drops its answer.
    var inputs = reads.ItemGeneration("planning-inputs", saved.TruckId);
    var connections = reads.ItemGeneration(SavedInputsFamily, saved.TruckId);
    if (
      shared!.TryGetValue(key, out var known)
      && known.Inputs == inputs
      && known.Connections == connections
    )
      return known.Matches;
    var matches = await MatchesAsync(remaining, dependencies, selected, ct);
    shared?[key] = new(inputs, connections, matches);
    return matches;
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
          if (!Replayed(batch, selected))
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

  private static bool Replayed(
    FuelHistoryBatch batch,
    HashSet<Guid> selected
  ) => batch.Inputs.Any(x => selected.Contains(x.Current.Id));
}
