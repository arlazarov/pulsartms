using System.Collections.Immutable;
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
        Freeze(
          await ApplyExecutionAsync(await reader.ReadAsync(ids, token), token)
        ),
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
    return scope.ReadAsync(
      async token =>
        Freeze(
          await ApplyExecutionAsync(
            await reader.ReadLoadedAsync(captured, token),
            token
          )
        ),
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
    if (nativeLegs.Count == 0)
      return history;
    var result = history.ToDictionary();
    foreach (var group in nativeLegs.GroupBy(x => x.TruckId))
    {
      var truck = group.Key;
      var snapshots = history
        .Values.Where(x => x.Current.TruckId == truck)
        .ToArray();
      var candidates = snapshots
        .SelectMany(x => x.Predecessors.Append(x.Current))
        .Select(x => x.Id)
        .Distinct()
        .ToArray();
      var execution = await ExecutionLoads.ReadAsync(
        db,
        truck,
        candidates,
        ct,
        completedLegIds: group
          .Where(x => x.Status == "completed")
          .Select(x => x.Id)
          .ToArray()
      );
      foreach (var snapshot in snapshots)
        result[snapshot.Current.Id] = snapshot with
        {
          Predecessors = snapshot
            .Predecessors.Where(x => !execution.OwnedDispatchIds.Contains(x.Id))
            .Concat(execution.Loads.Select(x => x.Work))
            .ToImmutableArray(),
        };
    }
    return result;
  }
}
