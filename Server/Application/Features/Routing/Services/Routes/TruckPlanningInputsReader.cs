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
  public Guid? DriverId { get; init; }
  public Guid? CoDriverId { get; init; }
  public bool CanUseGps { get; init; }
}

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
    var settings = await ReadProfilesAsync(ids, ct);
    var asOf = DateTimeOffset.UtcNow;
    var key =
      $"planning-work:{asOf.UtcDateTime:yyyy-MM-dd}:"
      + $"{reads.Generation("dispatch")}:{reads.Generation("execution")}:"
      + $"{reads.Generation("route-previews")}:"
      + $"{reads.Generation("settings")}:"
      + string.Join(',', ids);
    var captured = await reads.GetAsync(
      "board",
      key,
      () => CaptureAsync(ids, asOf, settings, ct),
      ct: ct
    );
    return await WithClocksAsync(captured, includeHos, ct);
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

  private async Task<Dictionary<Guid, TruckRouteProfile>> ReadProfilesAsync(
    IReadOnlyCollection<Guid> ids,
    CancellationToken ct
  )
  {
    // ReadCache stripes are not reentrant; resolve cached dependencies before
    // entering the work-cache factory.
    var result = new Dictionary<Guid, TruckRouteProfile>();
    foreach (var id in ids)
      result[id] = await profiles.GetAsync(id, ct);
    return result;
  }

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
        var saved = await savedRoutes.ReadManyAsync(
          segments.Select(x => x.Work.DispatchId).Distinct().ToArray(),
          token
        );
        var legIds = segments
          .Where(x => x.Work.ExecutionLegId.HasValue)
          .Select(x => x.Work.ExecutionLegId!.Value)
          .Distinct()
          .ToArray();
        var native =
          legIds.Length == 0
            ? new Dictionary<Guid, SavedRoutePlanMetadata>()
            : await savedRoutes.ReadExecutionLegsAsync(legIds, token);
        var current = new Dictionary<Guid, TruckWorkSegment?>();
        foreach (var snapshot in snapshots.Values)
        {
          var profile = settings[snapshot.TruckId];
          current[snapshot.TruckId] = PlanningWorkPolicy
            .Candidates(snapshot)
            .FirstOrDefault(segment =>
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
            );
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
