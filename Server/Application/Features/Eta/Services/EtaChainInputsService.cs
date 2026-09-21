using System.Collections.Immutable;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Application.Diagnostics;
using Application.Features.Dispatch.Models;
using Application.Features.Eta.Algorithms;
using Application.Features.Eta.Interfaces;
using Application.Features.Eta.Models;
using Application.Features.Eta.Options;
using Application.Features.Execution.Interfaces;
using Application.Features.Execution.Models;
using Application.Features.Execution.Services;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services.Deadheads;
using Application.Features.Routing.Services.Routes;
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
      var reason = Exclusion(item, loads, blocked);
      if (reason is { } excluded)
      {
        exclusions.Add(new(item.Work, excluded));
        blocked |=
          excluded
            is EtaWorkExclusionReason.NativeNotActive
              or EtaWorkExclusionReason.NativeConnectionRequired
              or EtaWorkExclusionReason.UnresolvedWork;
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

  private async Task<
    IReadOnlyDictionary<Guid, SavedNextLoadRoute>
  > FutureGeometryAsync(
    IReadOnlyList<RouteWorkSnapshot> future,
    CancellationToken ct
  )
  {
    var values = new Dictionary<Guid, SavedNextLoadRoute>();
    if (Plain(future) is { Length: > 0 } plain)
      foreach (
        var (id, route) in await savedRoutes.ReadGeometryAsync(plain, ct)
      )
        values[id] = route;
    // Asked for by leg and answered by leg; the chain knows loads, so the
    // answer is put back under the load it belongs to.
    if (Accepted(future) is { Length: > 0 } legs)
      foreach (
        var route in (
          await savedRoutes.ReadExecutionGeometryAsync(legs, ct)
        ).Values
      )
        values[route.DispatchId] = route;
    return values;
  }

  private static Guid[] Plain(IReadOnlyList<RouteWorkSnapshot> loads) =>
    loads.Where(x => !x.ExecutionLegId.HasValue).Select(x => x.Id).ToArray();

  private static Guid[] Accepted(IReadOnlyList<RouteWorkSnapshot> loads) =>
    loads
      .Where(x => x.ExecutionLegId.HasValue)
      .Select(x => x.ExecutionLegId!.Value)
      .ToArray();

  private static EtaWorkExclusionReason? Exclusion(
    TruckWorkSegment segment,
    IReadOnlyList<RouteWorkSnapshot> selected,
    bool blocked
  )
  {
    if (blocked)
      return EtaWorkExclusionReason.BlockedByEarlierWork;
    if (!segment.Work.ExecutionLegId.HasValue)
    {
      if (segment.Status is not ("assigned" or "in_transit"))
        return EtaWorkExclusionReason.LegacyNotAssigned;
      if (segment.IsOverdue)
        return EtaWorkExclusionReason.OverdueUpcoming;
    }
    else if (!PlanningWorkPolicy.HasOpenAssignment(segment))
      return EtaWorkExclusionReason.NativeNotActive;
    // Work accepted into execution ahead of this one is still this truck's
    // work, and the forecast can follow it: those stops carry their own
    // appointments and service, and the clock keeps its rests across them.
    // Where a run ends is one question, asked in one place - here and in the
    // fuel horizon alike.
    if (selected.Count > 0 && !PlanningWorkPolicy.ContinuesTheRun(segment))
      return EtaWorkExclusionReason.NativeConnectionRequired;
    return PlanningWorkPolicy.BlockingProblem(segment) is null
      ? null
      : EtaWorkExclusionReason.UnresolvedWork;
  }

  public async Task<EtaChainPlan> PrepareAsync(
    EtaChainDescription description,
    CancellationToken ct
  )
  {
    var timings = await memory.FutureTimingAsync(
      description.GeometryHash,
      async () =>
      {
        var future = description.Loads.Skip(1).ToArray();
        var saved = await FutureGeometryAsync(future, ct);
        RequireFutureRoads(
          description.Roads.Future,
          future.Select(load =>
            NextLoadRouteVersion.From(
              load.Id,
              load.ExecutionLegId,
              saved.GetValueOrDefault(load.Id)
            )
          )
        );
        var values = new List<EtaFutureTiming>();
        var previous = description.RootDispatchId;
        foreach (var load in future)
        {
          var item = saved.GetValueOrDefault(load.Id);
          var pair = description.Connections.GetValueOrDefault(load.Id);
          var connection =
            pair?.Previous.Id == previous
              ? pair.ReadRoute(item?.Deadhead, description.Profile)
              : null;
          var road =
            item?.BaseRoute is { } baseRoute
            && baseRoute.InputHash
              == BaseRouteService.Signature(load, description.Profile)
              ? SavedRouteReader.Route(
                baseRoute.RouteJson,
                load.Stops.Length - 1
              )
              : null;
          string? reason =
            connection is null
              ? "ETA unavailable: waiting for the saved connection from the preceding load."
            : road is null
              ? "ETA unavailable: waiting for the saved load route."
            : null;
          var routeTiming = road is null
            ? null
            : EtaRouteTiming.Compile(road, regions);
          var connectionTiming =
            connection is null
            || connection.Legs.All(leg => leg.Miles == 0 && leg.Seconds == 0)
              ? null
              : EtaRouteTiming.Compile(connection, regions);
          if (
            routeTiming?.HasCompleteTravelTimes == false
            || connectionTiming?.HasCompleteTravelTimes == false
          )
            reason = "ETA unavailable: incomplete saved road travel times.";
          var points =
            road is null || road.Legs.Count == 0
              ? ImmutableArray<RoutePoint>.Empty
              : road
                .Legs.Select(leg => leg.Points[^1])
                .Prepend(road.Legs[0].Points[0])
                .ToImmutableArray();
          values.Add(
            new(load.Id, connectionTiming, routeTiming, points, reason)
          );
          previous = load.Id;
        }
        return values.ToImmutableArray();
      },
      ct
    );
    var byId = timings.ToDictionary(x => x.DispatchId);
    var result = description
      .Loads.Skip(1)
      .Select(load =>
      {
        var value = byId[load.Id];
        var stops = load
          .Stops.OrderBy(s => s.Sequence)
          .Select(
            (s, i) =>
              new PlanStop(
                s.Id,
                s.Name,
                s.Address,
                s.Sequence,
                i < value.StopPoints.Length ? value.StopPoints[i] : new(0, 0)
              )
              {
                Job = s.Job,
                StateAfter = s.StateAfter,
                ScheduledDate = s.ScheduledDate,
                ScheduledTime = s.ScheduledTime,
                ScheduledDate2 = s.ScheduledDate2,
                ScheduledTime2 = s.ScheduledTime2,
                AppointmentTimeZoneId = s.AppointmentTimeZoneId,
              }
          )
          .ToArray();
        var driverChanged = DriverChanged(load, description.DriverId);
        return new EtaFutureDispatch(
          load.Id,
          stops,
          value.Connection,
          value.Route,
          SequenceReason(description, load)
            ?? (
              driverChanged
                ? "ETA unavailable: the next load has a different driver assignment."
                : value.UnavailableReason
            )
        );
      })
      .ToArray();
    return new(
      description.InputHash,
      result,
      description
        .Loads[0]
        .Stops.ToDictionary(
          s => s.Id,
          s => new EtaStopActivity(
            s.ArrivedAt,
            s.PickedUpAt,
            s.DeliveredAt,
            s.DepartedAt,
            s.ManualCompletedAt,
            s.CompletionOverride
          )
        )
    )
    {
      CurrentUnavailableReason =
        SequenceReason(description, description.Loads[0])
        ?? (
          DriverChanged(description.Loads[0], description.DriverId)
            ? "ETA unavailable: waiting for the receiving driver's truck assignment and HOS."
            : null
        ),
    };
  }

  private static string? SequenceReason(
    EtaChainDescription description,
    RouteWorkSnapshot load
  ) =>
    description
      .Sequence.Issues.FirstOrDefault(x =>
        x.Work == new WorkIdentity(load.Id, load.ExecutionLegId)
      )
      ?.Problem switch
    {
      WorkSequenceProblem.CompetingCurrentWork =>
        "ETA unavailable: multiple loads have started; confirm the current work.",
      WorkSequenceProblem.UnknownOrder =>
        "ETA unavailable: the order of assigned work is unresolved.",
      WorkSequenceProblem.ConflictingOrder =>
        "ETA unavailable: assigned work conflicts with its predecessor order.",
      WorkSequenceProblem.MissingExecutionLink =>
        "ETA unavailable: the execution link is missing.",
      WorkSequenceProblem.AwaitingTransfer =>
        "ETA unavailable: waiting for confirmed release and receipt.",
      _ => null,
    };

  private static bool DriverChanged(RouteWorkSnapshot load, Guid? driverId) =>
    load.DriverId.HasValue && load.DriverId != driverId
    || load.Stops.Any(s => !s.IsCompleted && s.DriverId != driverId);

  private static string Hash(object? value) =>
    Convert.ToHexString(
      SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value))
    );
}
