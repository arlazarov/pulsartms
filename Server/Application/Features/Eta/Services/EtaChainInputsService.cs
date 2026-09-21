using System.Collections.Immutable;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Application.Diagnostics;
using Application.Features.Dispatch.Models;
using Application.Features.Eta.Interfaces;
using Application.Features.Execution.Interfaces;
using Application.Features.Execution.Services;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Services.Deadheads;
using Application.Features.Routing.Services.Routes;
using Domain.Models.Eta;
using Domain.Models.Execution;
using Domain.Models.Routing;
using Domain.Policies;
using Domain.Rules.Eta;
using Domain.Rules.Ports;
using Domain.Rules.Routing;
using Microsoft.Extensions.Options;

namespace Application.Features.Eta.Services;

public sealed record EtaChainDescription(
  Guid TruckId,
  Guid RootDispatchId,
  string DriverExternalId,
  Guid? DriverId,
  string InputHash,
  string GeometryHash,
  ImmutableArray<RouteWorkSnapshot> Loads,
  TruckRouteProfile Profile,
  IReadOnlyDictionary<Guid, DeadheadConnection?> Connections,
  WorkSequenceAssessment Sequence,
  TruckItinerarySnapshot Itinerary,
  ImmutableArray<EtaWorkExclusion> Exclusions,
  DeadheadHistoryBatch History,
  EtaSavedRoadInputs Roads
)
{
  public Guid? RootExecutionLegId => Loads[0].ExecutionLegId;
  public Guid ScopeId => RootExecutionLegId ?? RootDispatchId;
}

public sealed record EtaFutureTiming(
  Guid DispatchId,
  EtaRouteTiming? Connection,
  EtaRouteTiming? Route,
  ImmutableArray<RoutePoint> StopPoints,
  string? UnavailableReason
);

public sealed partial class EtaChainInputsService(
  IAppDbContext db,
  TruckItineraryReader itineraries,
  IExecutionReadScope scope,
  TruckPlanningProfileService profiles,
  ISavedRoutePlanReader rootRoutes,
  INextLoadRouteReader savedRoutes,
  DeadheadHistoryService history,
  EtaMemory memory,
  IRouteRegionLookup regions,
  IOptions<EtaPlanningOptions> options
)
{
  private sealed record AssignedDriver(Guid? DriverId, string ExternalId);

  private sealed record ReadBatch(
    IReadOnlyDictionary<Guid, SavedRoutePlanMetadata> Roots,
    IReadOnlyDictionary<Guid, SavedRoutePlanMetadata> ExecutionRoots,
    IReadOnlyDictionary<Guid, AssignedDriver> Drivers
  );

  public Task<IReadOnlyDictionary<Guid, EtaChainDescription>> DescribeManyAsync(
    IReadOnlyList<TruckDispatchBoardResponse> rows,
    CancellationToken ct
  ) =>
    DescribeTrucksAsync(
      rows.Where(x => x.TruckId.HasValue)
        .Select(x => x.TruckId!.Value)
        .Distinct()
        .ToArray(),
      ct
    );

  public async Task<EtaChainDescription?> DescribeAsync(
    Guid truckId,
    CancellationToken ct
  ) => (await DescribeTrucksAsync([truckId], ct)).GetValueOrDefault(truckId);

  private Task<
    IReadOnlyDictionary<Guid, EtaChainDescription>
  > DescribeTrucksAsync(Guid[] truckIds, CancellationToken ct) =>
    scope.ReadAsync(
      async token =>
      {
        var at = Stopwatch.GetTimestamp();
        var snapshots = await itineraries.ReadManyAsync(
          truckIds,
          DateTimeOffset.UtcNow,
          token
        );
        PerformanceStages.Elapsed("eta-describe", "itineraries", at);
        var descriptions = new Dictionary<Guid, EtaChainDescription>();
        var work = snapshots.Values.Where(x => !x.Segments.IsEmpty).ToArray();
        if (work.Length == 0)
          return (IReadOnlyDictionary<Guid, EtaChainDescription>)descriptions;
        at = Stopwatch.GetTimestamp();
        var batch = await ReadBatchAsync(work, token);
        PerformanceStages.Elapsed("eta-describe", "batch", at);
        at = Stopwatch.GetTimestamp();
        foreach (var snapshot in work)
          if (
            await DescribeCoreAsync(snapshot, batch, token) is { } description
          )
            descriptions[snapshot.TruckId] = description;
        PerformanceStages.Elapsed("eta-describe", "core", at);
        PerformanceStages.Count("eta-describe", "trucks", work.Length);
        return descriptions;
      },
      ct,
      requireFreshSnapshot: true
    );

  internal Task RequireCurrentProfileAsync(
    EtaChainDescription expected,
    CancellationToken ct
  ) =>
    profiles.RequireRoutingCurrentAsync(
      expected.Loads[0],
      expected.Profile,
      ct
    );

  private async Task<ReadBatch> ReadBatchAsync(
    IReadOnlyList<TruckItinerarySnapshot> snapshots,
    CancellationToken ct
  )
  {
    var items = snapshots.SelectMany(x => x.Segments).ToArray();
    var ids = items.Select(x => x.Work.DispatchId).Distinct().ToArray();
    var legIds = items
      .Where(x => x.Work.ExecutionLegId.HasValue)
      .Select(x => x.Work.ExecutionLegId!.Value)
      .Distinct()
      .ToArray();
    var driverIds = items
      .Select(x => x.DriverId)
      .Concat(snapshots.Select(x => x.Resources.DriverId))
      .Where(x => x.HasValue)
      .Select(x => x!.Value)
      .Distinct()
      .ToArray();
    var drivers = await db
      .Drivers.AsNoTracking()
      .Where(x => driverIds.Contains(x.Id))
      .Select(x => new { x.Id, x.ExternalId })
      .ToDictionaryAsync(
        x => x.Id,
        x => new AssignedDriver(x.Id, x.ExternalId),
        ct
      );
    return new(
      await rootRoutes.ReadManyAsync(ids, ct),
      legIds.Length == 0
        ? new Dictionary<Guid, SavedRoutePlanMetadata>()
        : await rootRoutes.ReadExecutionLegsAsync(legIds, ct),
      drivers
    );
  }

  private async Task<EtaChainDescription?> DescribeCoreAsync(
    TruckItinerarySnapshot itinerary,
    ReadBatch batch,
    CancellationToken ct
  )
  {
    var truckId = itinerary.TruckId;
    var profile = await profiles.GetAsync(truckId, ct);
    var loads = new List<RouteWorkSnapshot>();
    var roots = ImmutableArray.CreateBuilder<EtaRootRoadVersion>();
    var exclusions = ImmutableArray.CreateBuilder<EtaWorkExclusion>();
    var blocked = false;
    foreach (var item in itinerary.Segments)
    {
      var reason = EtaWorkSelection.Exclusion(item, loads, blocked);
      if (reason is { } excluded)
      {
        exclusions.Add(new(item.Work, excluded));
        blocked |= EtaWorkSelection.EndsTheRun(excluded);
        continue;
      }
      var load = RouteWorkProjection.Capture(
        item,
        itinerary.Resources.TruckNumber
      );
      if (loads.Count == 0)
      {
        var saved = load.ExecutionLegId is { } legId
          ? batch.ExecutionRoots.GetValueOrDefault(legId)
          : batch.Roots.GetValueOrDefault(load.Id);
        roots.Add(RootVersion(item.Work, saved));
        if (PlanningWorkPolicy.IsCompleted(saved, load, profile))
        {
          exclusions.Add(
            new(item.Work, EtaWorkExclusionReason.SavedRouteCompleted)
          );
          continue;
        }
      }
      loads.Add(load);
    }
    if (loads.Count == 0)
      return null;
    var sequence = WorkSequencePolicy.Assess(loads, itinerary.Evidence);
    var future = loads.Skip(1).ToArray();
    var futureIds = future.Select(x => x.Id).ToArray();
    var versions =
      future.Length == 0 ? [] : await FutureVersionsAsync(future, ct);
    var predecessors =
      future.Length == 0
        ? new Dictionary<Guid, DeadheadHistorySnapshot>()
        : await history.ReadLoadedAsync(future, ct);
    var connections = future.ToDictionary(
      x => x.Id,
      x => DeadheadConnection.Find(predecessors.GetValueOrDefault(x.Id))
    );
    var driverId = loads[0].ExecutionLegId.HasValue
      ? loads[0].DriverId
      : itinerary.Resources.DriverId;
    var driver = driverId.HasValue
      ? batch.Drivers.GetValueOrDefault(driverId.Value)
      : null;
    var geometryHash = Hash(
      new
      {
        TruckId = truckId,
        Root = loads[0].Id,
        loads[0].ExecutionLegId,
        Versions = versions.OrderBy(x => x.DispatchId),
        Future = future.Select(x => new
        {
          x.Id,
          Base = BaseRouteService.Signature(x, profile),
          Connection = connections[x.Id]?.Signature(profile),
          Stops = x.Stops.OrderBy(s => s.Sequence).Select(s => s.Id),
        }),
      }
    );
    var roads = new EtaSavedRoadInputs(
      roots.ToImmutable(),
      versions.OrderBy(x => x.DispatchId).ToImmutableArray()
    );
    var inputHash = Hash(
      new
      {
        Policy = 16,
        History = predecessors
          .Values.OrderBy(x => x.Current.Id)
          .Select(x => new { x.Current.Id, x.InputSignature }),
        itinerary.InputSignature,
        Exclusions = exclusions.ToImmutable(),
        TruckId = truckId,
        Driver = driver,
        Roots = roads.Roots,
        RootInput = RoutePlanInputs.Hash(loads[0], profile),
        Sequence = sequence,
        Geometry = geometryHash,
        Planning = options.Value,
      }
    );
    return new(
      truckId,
      loads[0].Id,
      driver?.ExternalId ?? "",
      driver?.DriverId,
      inputHash,
      geometryHash,
      loads.ToImmutableArray(),
      profile,
      connections,
      sequence,
      itinerary,
      exclusions.ToImmutable(),
      new(predecessors.Values.OrderBy(x => x.Current.Id).ToImmutableArray()),
      roads
    );
  }

  // A load's saved roads live where the load does: under its dispatch while
  // it is only assigned, under its execution leg once it has been accepted.
  // Reading only the first kind was right while work ahead was never
  // accepted in advance; now it is, and that read answered "no roads" for a
  // load whose roads were sitting under its leg.
  private Task<IReadOnlyList<NextLoadRouteVersion>> FutureVersionsAsync(
    IReadOnlyList<RouteWorkSnapshot> future,
    CancellationToken ct
  ) => FutureVersionsAsync(Plain(future), Accepted(future), ct);

  private async Task<IReadOnlyList<NextLoadRouteVersion>> FutureVersionsAsync(
    Guid[] plain,
    Guid[] accepted,
    CancellationToken ct
  )
  {
    var values = new List<NextLoadRouteVersion>();
    if (plain.Length > 0)
      values.AddRange(await savedRoutes.ReadVersionsAsync(plain, ct));
    if (accepted.Length > 0)
      values.AddRange(
        await savedRoutes.ReadExecutionVersionsAsync(accepted, ct)
      );
    return values;
  }

  private static Guid[] Plain(IReadOnlyList<RouteWorkSnapshot> loads) =>
    loads.Where(x => !x.ExecutionLegId.HasValue).Select(x => x.Id).ToArray();

  private static Guid[] Accepted(IReadOnlyList<RouteWorkSnapshot> loads) =>
    loads
      .Where(x => x.ExecutionLegId.HasValue)
      .Select(x => x.ExecutionLegId!.Value)
      .ToArray();

  private static string Hash(object? value) =>
    Convert.ToHexString(
      SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value))
    );
}
