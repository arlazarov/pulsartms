using System.Collections.Immutable;
using System.Diagnostics;
using Application.Diagnostics;
using Application.Features.Execution.Queries;
using Domain.Models.Routing;

namespace Application.Features.Routing.Services.Deadheads;

public sealed partial class DeadheadHistoryService
{
  private async Task<
    IReadOnlyList<IReadOnlyDictionary<Guid, DeadheadHistorySource>>
  > ApplyExecutionBatchesAsync(
    IReadOnlyList<IReadOnlyDictionary<Guid, DeadheadHistorySource>> batches,
    CancellationToken ct
  )
  {
    var all = batches.SelectMany(x => x.Values).ToArray();
    var trucks = all.Where(x => x.Current.TruckId.HasValue)
      .Select(x => x.Current.TruckId!.Value)
      .Distinct()
      .ToArray();
    if (trucks.Length == 0)
      return batches;
    var ids = all.Select(x => x.Current.Id).Distinct().ToArray();
    var predecessors = all.SelectMany(x => x.Predecessors)
      .Select(x => x.Id)
      .Distinct()
      .ToArray();
    var started = Stopwatch.GetTimestamp();
    var legs = await db
      .ExecutionLegs.AsNoTracking()
      .Where(x =>
        trucks.Contains(x.TruckId)
        && (
          x.Status == "active"
          || x.Status == "planned"
          || x.Status == "completed"
            && (
              x.Loads.Any(link => predecessors.Contains(link.DispatchId))
              || db.DispatchDeadheads.Any(saved =>
                ids.Contains(saved.DispatchId)
                && saved.PreviousExecutionLegId == x.Id
              )
            )
        )
      )
      .Select(x => new
      {
        x.Id,
        x.TruckId,
        x.Status,
        Predecessors = x
          .Loads.Where(link => predecessors.Contains(link.DispatchId))
          .Select(link => link.DispatchId)
          .ToArray(),
        SavedFor = db
          .DispatchDeadheads.Where(saved =>
            ids.Contains(saved.DispatchId)
            && saved.PreviousExecutionLegId == x.Id
          )
          .Select(saved => saved.DispatchId)
          .ToArray(),
      })
      .ToListAsync(ct);
    PerformanceStages.Elapsed("deadhead-history", "legs", started);
    if (legs.Count == 0)
      return batches;
    started = Stopwatch.GetTimestamp();
    var execution = await ExecutionLoads.ReadAsync(
      db,
      names,
      transfers,
      null,
      all.SelectMany(x => x.Predecessors.Append(x.Current))
        .Select(x => x.Id)
        .Distinct()
        .ToArray(),
      ct,
      completedLegIds: legs.Where(x => x.Status == "completed")
        .Select(x => x.Id)
        .ToArray(),
      truckIds: legs.Select(x => x.TruckId).Distinct().ToArray()
    );
    PerformanceStages.Elapsed("deadhead-history", "loads", started);
    var result = new List<IReadOnlyDictionary<Guid, DeadheadHistorySource>>();
    for (var index = 0; index < batches.Count; index++)
    {
      var batch = batches[index];
      var applied = batch.ToDictionary();
      foreach (var snapshot in batch.Values)
      {
        if (snapshot.Current.TruckId is not { } truck)
          continue;
        var ownPredecessors = snapshot
          .Predecessors.Select(x => x.Id)
          .ToHashSet();
        var eligible = legs.Where(x =>
            x.TruckId == truck
            && (
              x.Status != "completed"
              || x.Predecessors.Any(ownPredecessors.Contains)
              || x.SavedFor.Contains(snapshot.Current.Id)
            )
          )
          .Select(x => x.Id)
          .ToHashSet();
        if (eligible.Count == 0)
          continue;
        var loads = execution
          .Loads.Where(x =>
            x.Work.TruckId == truck
            && x.Work.ExecutionLegId is { } leg
            && eligible.Contains(leg)
          )
          .Select(x => x.Work);
        // Completed history belongs to this work item, not its lookup peers.
        applied[snapshot.Current.Id] = snapshot with
        {
          Predecessors = snapshot
            .Predecessors.Where(x => !execution.OwnedDispatchIds.Contains(x.Id))
            .Concat(loads)
            .ToImmutableArray(),
        };
      }
      result.Add(applied);
    }
    return result;
  }
}
