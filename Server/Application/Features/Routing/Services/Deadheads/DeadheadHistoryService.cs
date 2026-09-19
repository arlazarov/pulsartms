using System.Collections.Immutable;
using System.Diagnostics;
using Application.Diagnostics;
using Application.Features.Execution.Interfaces;
using Application.Features.Execution.Queries;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services.Routes;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Application.Features.Routing.Services.Deadheads;

public sealed class DeadheadHistoryService(
  IAppDbContext db,
  IDeadheadHistoryReader reader,
  IExecutionReadScope scope
)
{
  public Task<IReadOnlyDictionary<Guid, DeadheadHistorySnapshot>> ReadAsync(
    IReadOnlyCollection<Guid> ids,
    CancellationToken ct
  ) =>
    scope.ReadAsync(
      async token =>
      {
        var started = Stopwatch.GetTimestamp();
        var sources = await reader.ReadAsync(ids, token);
        PerformanceStages.Elapsed("deadhead-history", "reader", started);
        started = Stopwatch.GetTimestamp();
        var applied = await ApplyExecutionAsync(sources, token);
        PerformanceStages.Elapsed("deadhead-history", "execution", started);
        started = Stopwatch.GetTimestamp();
        var frozen = Freeze(applied);
        PerformanceStages.Elapsed("deadhead-history", "freeze", started);
        return frozen;
      },
      ct
    );

  public Task<
    IReadOnlyDictionary<Guid, DeadheadHistorySnapshot>
  > ReadLoadedAsync(IReadOnlyCollection<Load> loads, CancellationToken ct)
  {
    var captured = loads.Select(RouteWorkProjection.Capture).ToArray();
    return ReadLoadedAsync(captured, ct);
  }

  public Task<
    IReadOnlyDictionary<Guid, DeadheadHistorySnapshot>
  > ReadLoadedAsync(
    IReadOnlyCollection<RouteWorkSnapshot> loads,
    CancellationToken ct
  )
  {
    var captured = loads.Select(RouteWorkProjection.TruckItinerary).ToArray();
    var opened = Stopwatch.GetTimestamp();
    return scope.ReadAsync(
      async token =>
      {
        PerformanceStages.Elapsed("deadhead-history", "scope", opened);
        var started = Stopwatch.GetTimestamp();
        var sources = await reader.ReadLoadedAsync(captured, token);
        PerformanceStages.Elapsed("deadhead-history", "reader", started);
        started = Stopwatch.GetTimestamp();
        var applied = await ApplyExecutionAsync(sources, token);
        PerformanceStages.Elapsed("deadhead-history", "execution", started);
        started = Stopwatch.GetTimestamp();
        var frozen = Freeze(applied);
        PerformanceStages.Elapsed("deadhead-history", "freeze", started);
        return frozen;
      },
      ct
    );
  }

  public async Task<
    IReadOnlyDictionary<(Guid, Guid?), DeadheadHistorySnapshot>
  > ReadSectionsAsync(
    IReadOnlyCollection<RouteWorkSnapshot> loads,
    CancellationToken ct
  )
  {
    var result = new Dictionary<(Guid, Guid?), DeadheadHistorySnapshot>();
    var groups = loads.GroupBy(x => x.Id).Select(x => x.ToArray()).ToArray();
    for (var index = 0; groups.Any(x => x.Length > index); index++)
    {
      var batch = groups.Where(x => x.Length > index).Select(x => x[index]);
      var snapshots = await ReadLoadedAsync(batch.ToArray(), ct);
      foreach (var snapshot in snapshots.Values)
        result.Add(
          (snapshot.Current.Id, snapshot.Current.ExecutionLegId),
          snapshot
        );
    }
    return result;
  }

  public Task<DeadheadHistorySnapshot?> ReadAsync(
    RouteWorkSnapshot expected,
    CancellationToken ct
  ) =>
    scope.ReadAsync<DeadheadHistorySnapshot?>(
      async token =>
      {
        if (expected.ExecutionLegId is null)
          return (await ReadAsync([expected.Id], token)).GetValueOrDefault(
            expected.Id
          );
        var source = await db
          .Dispatches.AsNoTracking()
          .Include(x => x.Stops)
          .SingleOrDefaultAsync(x => x.Id == expected.Id, token);
        if (source is null)
          return null;
        var sections = await ExecutionRouteSections.ReadAsync(
          db,
          [source],
          token
        );
        var current = sections
          .GetValueOrDefault(source.Id)
          ?.SingleOrDefault(x => x.ExecutionLegId == expected.ExecutionLegId);
        return current is null
          ? null
          : (await ReadLoadedAsync([current], token)).GetValueOrDefault(
            current.Id
          );
      },
      ct
    );

  public Task<bool> MatchesAsync(
    DeadheadHistorySnapshot expected,
    CancellationToken ct
  ) =>
    scope.ReadAsync(
      async token =>
      {
        var current = await ReadAsync(expected.Current, token);
        return current?.InputSignature == expected.InputSignature;
      },
      ct,
      requireFreshSnapshot: true
    );

  internal async Task RequirePredecessorsAsync(
    IReadOnlyCollection<DeadheadHistoryBatch> batches,
    CancellationToken ct
  )
  {
    if (batches.Count == 0)
      return;
    if (db.Database.CurrentTransaction is null)
      throw new InvalidOperationException(
        "Historical validation requires the work publication transaction."
      );
    // Current work is validated by the publication owner. Native completed-leg
    // selection shares references within each original lookup batch.
    foreach (var batch in batches)
    {
      if (batch.Snapshots.IsEmpty)
        continue;
      var current = await ReadLoadedAsync(
        batch.Snapshots.Select(x => x.Current).ToArray(),
        ct
      );
      if (
        batch.Snapshots.Any(x =>
          current.GetValueOrDefault(x.Current.Id)?.InputSignature
          != x.InputSignature
        )
      )
        throw new RoutePlanningException(
          "Historical truck work changed. Refresh the connection inputs."
        );
    }
  }

  private static IReadOnlyDictionary<Guid, DeadheadHistorySnapshot> Freeze(
    IReadOnlyDictionary<Guid, DeadheadHistorySource> sources
  ) =>
    sources.ToImmutableDictionary(
      x => x.Key,
      x => DeadheadHistoryProjection.Capture(x.Value)
    );

  private async Task<
    IReadOnlyDictionary<Guid, DeadheadHistorySource>
  > ApplyExecutionAsync(
    IReadOnlyDictionary<Guid, DeadheadHistorySource> history,
    CancellationToken ct
  )
  {
    var trucks = history
      .Values.Where(x => x.Current.TruckId.HasValue)
      .Select(x => x.Current.TruckId!.Value)
      .Distinct()
      .ToArray();
    if (trucks.Length == 0)
      return history;
    var ids = history.Keys.ToArray();
    var predecessorIds = history
      .Values.SelectMany(x => x.Predecessors)
      .Select(x => x.Id)
      .Distinct()
      .ToArray();
    var legsStarted = Stopwatch.GetTimestamp();
    var nativeLegs = await db
      .ExecutionLegs.AsNoTracking()
      .Where(x =>
        trucks.Contains(x.TruckId)
        && (
          x.Status == "active"
          || x.Status == "planned"
          || x.Status == "completed"
            && (
              x.Loads.Any(link => predecessorIds.Contains(link.DispatchId))
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
      })
      .ToListAsync(ct);
    PerformanceStages.Elapsed("deadhead-history", "legs", legsStarted);
    if (nativeLegs.Count == 0)
      return history;
    var result = history.ToDictionary();
    var groups = nativeLegs.GroupBy(x => x.TruckId).ToArray();
    var byTruck = groups.ToDictionary(
      group => group.Key,
      group =>
        history.Values.Where(x => x.Current.TruckId == group.Key).ToArray()
    );

    // One read for every truck rather than one read per truck. This was a
    // loop of awaits, each about half a second, so the cost grew with the
    // fleet: two trucks meant two round trips, ten would mean ten. The read
    // already accepted a set of trucks.
    //
    // Widening the arguments is safe because each one is matched by id:
    // owned dispatches are looked up by dispatch id, completed legs by leg
    // id, and a leg belongs to one truck, so the union answers each truck
    // exactly as its own call did.
    var read = Stopwatch.GetTimestamp();
    var execution = await ExecutionLoads.ReadAsync(
      db,
      null,
      byTruck
        .Values.SelectMany(snapshots =>
          snapshots.SelectMany(x => x.Predecessors.Append(x.Current))
        )
        .Select(x => x.Id)
        .Distinct()
        .ToArray(),
      ct,
      completedLegIds: nativeLegs
        .Where(x => x.Status == "completed")
        .Select(x => x.Id)
        .ToArray(),
      truckIds: groups.Select(x => x.Key).ToArray()
    );
    PerformanceStages.Elapsed("deadhead-history", "loads", read);

    // Work.TruckId is the leg's truck, so the one result separates back into
    // what each truck's call would have returned.
    var loadsByTruck = execution
      .Loads.Where(x => x.Work.TruckId.HasValue)
      .GroupBy(x => x.Work.TruckId!.Value)
      .ToDictionary(x => x.Key, x => x.ToArray());
    foreach (var (truck, snapshots) in byTruck)
    {
      var loads = loadsByTruck.GetValueOrDefault(truck, []);
      foreach (var snapshot in snapshots)
        result[snapshot.Current.Id] = snapshot with
        {
          Predecessors = snapshot
            .Predecessors.Where(x => !execution.OwnedDispatchIds.Contains(x.Id))
            .Concat(loads.Select(x => x.Work))
            .ToImmutableArray(),
        };
    }
    return result;
  }
}
