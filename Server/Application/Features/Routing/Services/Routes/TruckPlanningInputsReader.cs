using Application.Caching;
using Application.Features.Execution.Interfaces;
using Application.Features.Execution.Services;
using Application.Features.Fleet.Interfaces;
using Application.Features.Routing.Interfaces;
using Domain.Models.Execution;
using Domain.Models.Fleet;
using Domain.Models.Routing;
using Domain.Rules;
using Domain.Rules.Routing;

namespace Application.Features.Routing.Services.Routes;

public sealed record TruckPlanningInputs(
  TruckItinerarySnapshot Itinerary,
  DriverHosClocks? Hos
)
{
  public WorkIdentity? CurrentWork { get; init; }

  // The segment of the current work: the one answer to "which work is this
  // truck on" that a reader of these inputs may use. The first candidate is
  // not it - a candidate planning has moved past comes first too.
  public TruckWorkSegment? CurrentSegment =>
    CurrentWork is { } work
      ? Itinerary.Segments.FirstOrDefault(x => x.Work == work)
      : null;

  // The assignment the current work was chosen at, so a consumer that read
  // the truck's work separately can tell a reassignment from the same work.
  public long? CurrentAssignmentRevision { get; init; }

  // The candidates ahead of the current work whose saved plans say they
  // are done, each at the assignment it was judged at: the rows planning
  // moved past, not merely the rows that come first.
  public IReadOnlyList<PassedWork> PassedWork { get; init; } = [];
  public Guid? DriverId { get; init; }
  public Guid? CoDriverId { get; init; }
  public bool CanUseGps { get; init; }
}

public sealed record PassedWork(WorkIdentity Work, long AssignmentRevision);

public sealed class TruckPlanningInputsReader(
  IAppDbContext db,
  TruckItineraryReader itineraries,
  IExecutionReadScope scope,
  ReadCache reads,
  IDriverHosProvider hos,
  ISavedRoutePlanReader savedRoutes,
  TruckPlanningProfileService profiles
)
{
  public async Task<TruckPlanningInputs?> ReadAsync(
    Guid truckId,
    CancellationToken ct,
    bool includeHos = true
  ) =>
    (await ReadManyAsync([truckId], ct, includeHos)).GetValueOrDefault(truckId);

  // The version of the truck's cached inputs, taken before reading them, so
  // a reader that finds them out of date can say which entry it found.
  public long Version(Guid truckId) =>
    reads.ItemGeneration("planning-inputs", truckId);

  // For a reader that found its inputs out of date - the current work's
  // plan passed after they were captured. The entry it read (seen) is
  // dropped and captured again; readers that found the same entry share
  // that one capture, and the next reader meets the new entry.
  public async Task<TruckPlanningInputs?> ReadAgainAsync(
    Guid truckId,
    long seen,
    CancellationToken ct,
    bool includeHos = true
  )
  {
    await reads.InvalidateItemIfUnchangedAsync(
      "planning-inputs",
      truckId,
      seen,
      ct
    );
    return await ReadAsync(truckId, ct, includeHos);
  }

  public async Task<
    IReadOnlyDictionary<Guid, TruckPlanningInputs>
  > ReadManyAsync(
    IReadOnlyCollection<Guid> truckIds,
    CancellationToken ct,
    bool includeHos = true
  )
  {
    ct.ThrowIfCancellationRequested();
    if (truckIds.Count == 0)
      return new Dictionary<Guid, TruckPlanningInputs>();
    var ids = truckIds.Distinct().Order().ToArray();
    var asOf = DateTimeOffset.UtcNow;
    var key =
      $"{asOf.UtcDateTime:yyyy-MM-dd}:"
      + $"{reads.Generation(ReadGroups.FleetCatalog)}:"
      + $"{reads.Generation(ReadGroups.Settings)}";
    var captured = await reads.GetManyAsync<CapturedWork>(
      "planning-inputs",
      ids,
      key,
      async missing =>
        await CaptureAsync(
          missing,
          asOf,
          await ReadProfilesAsync(missing, ct),
          ct
        ),
      ct
    );
    return await WithClocksAsync(captured, includeHos, ct);
  }

  // Trucks with running work: an execution leg that is active or planned,
  // or an older in-transit load that never had a leg. Their summaries are
  // kept prepared in the background. Finished and cancelled work is not
  // running work, so a truck's history never enters this set; trucks already
  // driving come first when the bound is reached.
  public async Task<IReadOnlyList<Guid>> RunningTruckIdsAsync(
    int limit,
    CancellationToken ct
  )
  {
    ArgumentOutOfRangeException.ThrowIfNegative(limit);
    if (limit == 0)
      return [];
    var legs = db
      .ExecutionLegs.AsNoTracking()
      .Where(x => x.Status == "active" || x.Status == "planned")
      .Select(x => new { x.TruckId, Rank = x.Status == "active" ? 0 : 1 });
    var loads = db
      .Dispatches.AsNoTracking()
      .Where(x =>
        x.Status == "in_transit"
        && x.TruckId != null
        && !db.LoadExecutionLegs.Any(link => link.DispatchId == x.Id)
      )
      .Select(x => new { TruckId = x.TruckId!.Value, Rank = 0 });
    return await legs.Concat(loads)
      .GroupBy(x => x.TruckId)
      .Select(x => new { TruckId = x.Key, Rank = x.Min(y => y.Rank) })
      .OrderBy(x => x.Rank)
      .ThenBy(x => x.TruckId)
      .Take(limit)
      .Select(x => x.TruckId)
      .ToListAsync(ct);
  }

  public async Task<TruckPlanningInputs?> ReadFreshAsync(
    Guid truckId,
    CancellationToken ct,
    bool includeHos = false,
    DateTimeOffset? asOf = null
  )
  {
    var settings = await ReadProfilesAsync([truckId], ct);
    var captured = await CaptureAsync(
      [truckId],
      asOf ?? DateTimeOffset.UtcNow,
      settings,
      ct,
      requireFreshSnapshot: true
    );
    return (await WithClocksAsync(captured, includeHos, ct)).GetValueOrDefault(
      truckId
    );
  }

  public async Task RequireCurrentAsync(
    TruckItinerarySnapshot snapshot,
    CancellationToken ct
  )
  {
    if (!await itineraries.MatchesAsync(snapshot, ct))
      throw new RoutePlanningException(
        "The truck work changed. Refresh and calculate the route again."
      );
  }

  private Task<IReadOnlyDictionary<Guid, TruckRouteProfile>> ReadProfilesAsync(
    IReadOnlyCollection<Guid> ids,
    CancellationToken ct
  ) => profiles.GetManyAsync(ids, ct);

  private Task<Dictionary<Guid, CapturedWork>> CaptureAsync(
    IReadOnlyCollection<Guid> ids,
    DateTimeOffset asOf,
    IReadOnlyDictionary<Guid, TruckRouteProfile> settings,
    CancellationToken ct,
    bool requireFreshSnapshot = false
  ) =>
    scope.ReadAsync(
      async token =>
      {
        var snapshots = await itineraries.ReadManyAsync(ids, asOf, token);
        var segments = snapshots.Values.SelectMany(x => x.Segments).ToArray();
        var plans = await savedRoutes.ReadWorkAsync(
          segments.Select(x => x.Work.DispatchId).Distinct().ToArray(),
          segments
            .Where(x => x.Work.ExecutionLegId.HasValue)
            .Select(x => x.Work.ExecutionLegId!.Value)
            .Distinct()
            .ToArray(),
          token
        );
        var saved = plans.Loads;
        var native = plans.Legs;
        var current = new Dictionary<Guid, TruckWorkSegment?>();
        var passed = new Dictionary<Guid, List<TruckWorkSegment>>();
        foreach (var snapshot in snapshots.Values)
        {
          var profile = settings[snapshot.TruckId];
          var done = passed[snapshot.TruckId] = [];
          current[snapshot.TruckId] = null;
          foreach (var segment in PlanningWorkPolicy.Candidates(snapshot))
          {
            if (
              !PlanningWorkPolicy.IsCompleted(
                segment.Work.ExecutionLegId is { } leg
                  ? native.GetValueOrDefault(leg)
                  : saved.GetValueOrDefault(segment.Work.DispatchId),
                RouteWorkProjection.Capture(
                  segment,
                  snapshot.Resources.TruckNumber
                ),
                profile
              )
            )
            {
              current[snapshot.TruckId] = segment;
              break;
            }
            done.Add(segment);
          }
        }
        var driverIds = snapshots
          .Values.Select(x => Driver(x, current[x.TruckId]))
          .Where(x => x.HasValue)
          .Select(x => x!.Value)
          .Distinct()
          .ToArray();
        var drivers =
          driverIds.Length == 0
            ? new Dictionary<Guid, string>()
            : await db
              .Drivers.AsNoTracking()
              .Where(x => driverIds.Contains(x.Id))
              .ToDictionaryAsync(x => x.Id, x => x.ExternalId, token);
        return snapshots.ToDictionary(
          x => x.Key,
          x => new CapturedWork(
            x.Value,
            current[x.Key],
            passed[x.Key],
            Driver(x.Value, current[x.Key]) is { } id
              ? drivers.GetValueOrDefault(id)
              : null
          )
        );
      },
      ct,
      requireFreshSnapshot
    );

  private async Task<
    IReadOnlyDictionary<Guid, TruckPlanningInputs>
  > WithClocksAsync(
    IReadOnlyDictionary<Guid, CapturedWork> captured,
    bool includeHos,
    CancellationToken ct
  )
  {
    var clocks = includeHos ? await hos.GetClocksAsync(ct) : null;
    return captured.ToDictionary(
      x => x.Key,
      x =>
      {
        var externalId = x.Value.DriverExternalId;
        return new TruckPlanningInputs(
          x.Value.Itinerary,
          externalId is not null ? clocks?.GetValueOrDefault(externalId) : null
        )
        {
          CurrentWork = x.Value.Current?.Work,
          CurrentAssignmentRevision = x.Value.Current?.AssignmentRevision,
          PassedWork =
          [
            .. x.Value.Passed.Select(segment => new PassedWork(
              segment.Work,
              segment.AssignmentRevision
            )),
          ],
          DriverId = Driver(x.Value.Itinerary, x.Value.Current),
          CoDriverId = x.Value.Current?.CoDriverId,
          CanUseGps =
            x.Value.Current is { } segment
            && PlanningWorkPolicy.CanUseGps(segment),
        };
      }
    );
  }

  private sealed record CapturedWork(
    TruckItinerarySnapshot Itinerary,
    TruckWorkSegment? Current,
    IReadOnlyList<TruckWorkSegment> Passed,
    string? DriverExternalId
  );

  private static Guid? Driver(
    TruckItinerarySnapshot snapshot,
    TruckWorkSegment? segment
  ) =>
    segment is null
    || !PlanningWorkPolicy.HasOpenAssignment(segment)
    || PlanningWorkPolicy.BlockingProblem(segment).HasValue
      ? null
    : segment.Work.ExecutionLegId.HasValue ? segment.DriverId
    : snapshot.Resources.DriverId;
}
