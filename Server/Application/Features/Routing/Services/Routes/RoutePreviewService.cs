using System.Text.Json;
using Application.Caching;
using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Queries;
using Application.Features.Execution.Models;
using Application.Features.Execution.Services;
using Application.Features.Fleet.Queries.GetFleetLocations;
using Application.Features.Fleet.Services;
using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Models;
using Microsoft.Extensions.Caching.Memory;

namespace Application.Features.Routing.Services.Routes;

public sealed class RoutePreviewService(
  IAppDbContext db,
  TruckPlanningInputsReader inputs,
  ISender mediator,
  ReadCache reads,
  RouteDisplayCache displays,
  RoutePlanningService routes,
  IMemoryCache cache,
  ServerTelemetry serverTelemetry,
  FleetTelemetryCache telemetryCache
)
{
  private const string CacheKey = "route-preview:fleet";
  private static readonly SemaphoreSlim FleetGate = new(1);

  private sealed record SavedPreview(string Generation, byte[] Json);

  private sealed record PreviewPlan(RoutePlan Plan, RouteGeometry Geometry);

  public async Task<List<AutomaticPlanningResult>> GetAsync(
    CancellationToken ct
  )
  {
    ct.ThrowIfCancellationRequested();
    var generation = Generation();
    if (ReadCached(generation) is { } hit)
      return hit;
    await FleetGate.WaitAsync(ct);
    try
    {
      generation = Generation();
      if (ReadCached(generation) is { } ready)
        return ready;
      var result = await LoadAsync(ct);
      var json = JsonSerializer.SerializeToUtf8Bytes(
        result,
        RoutingJson.Options
      );
      if (json.Length <= 8 * 1024 * 1024 && generation == Generation())
        cache.Set(
          CacheKey,
          new SavedPreview(generation, json),
          TimeSpan.FromSeconds(30)
        );
      return result;
    }
    finally
    {
      FleetGate.Release();
    }
  }

  private string Generation() =>
    $"{DateOnly.FromDateTime(DateTime.UtcNow):O}:"
    + $"{reads.Generation("board")}:{reads.Generation("dispatch")}:"
    + $"{reads.Generation("settings")}:{reads.Generation("route-previews")}:"
    + $"{reads.Generation("execution")}";

  private List<AutomaticPlanningResult>? ReadCached(string generation) =>
    cache.TryGetValue<SavedPreview>(CacheKey, out var saved)
    && saved!.Generation == generation
      ? JsonSerializer.Deserialize<List<AutomaticPlanningResult>>(
        saved.Json,
        RoutingJson.Options
      )
      : null;

  public async Task<AutomaticPlanningResult> ForTruckAsync(
    Guid truckId,
    CancellationToken ct
  )
  {
    ct.ThrowIfCancellationRequested();
    var work = await inputs.ReadAsync(truckId, ct, includeHos: false);
    return work is null
      ? NoRemaining(truckId)
      : await ReadRowAsync(work.Itinerary, null, ct);
  }

  private async Task<List<AutomaticPlanningResult>> LoadAsync(
    CancellationToken ct
  )
  {
    var rows = new List<TruckDispatchBoardResponse>();
    for (var page = 1; ; page++)
    {
      var board = await mediator.Send(
        new GetDispatchBoardQuery(
          Page: page,
          PageSize: 100,
          IncludeHos: false,
          IncludeFinancials: false,
          IncludeEta: false
        ),
        ct
      );
      if (!board.Success || board.Response is null)
        throw new RoutePlanningException(
          "Dispatch assignments are temporarily unavailable."
        );
      rows.AddRange(board.Response.Items);
      if (!board.Response.HasNextPage)
        break;
    }
    var snapshots = await inputs.ReadManyAsync(
      rows.Where(x => x.TruckId.HasValue)
        .Select(x => x.TruckId!.Value)
        .Distinct()
        .ToArray(),
      ct,
      includeHos: false
    );
    var ids = snapshots
      .Values.SelectMany(x => PlanningWorkPolicy.Candidates(x.Itinerary))
      .Select(x => x.Work.DispatchId)
      .Distinct()
      .ToArray();
    var saved = (
      await db
        .DispatchRoutePlans.AsNoTracking()
        .Where(x => ids.Contains(x.DispatchId))
        .Select(x => new { x.DispatchId, x.ExecutionLegId })
        .ToListAsync(ct)
    )
      .Select(x => (x.DispatchId, x.ExecutionLegId))
      .ToHashSet();
    var results = new List<AutomaticPlanningResult>();
    foreach (var snapshot in snapshots.Values)
    {
      try
      {
        var result = await ReadRowAsync(snapshot.Itinerary, saved, ct);
        if (result.State is not null)
          results.Add(result);
      }
      catch (RoutePlanningException) { }
    }
    return results;
  }

  private async Task<AutomaticPlanningResult> ReadRowAsync(
    TruckItinerarySnapshot snapshot,
    IReadOnlySet<(Guid DispatchId, Guid? ExecutionLegId)>? savedIds,
    CancellationToken ct
  )
  {
    var truckId = snapshot.TruckId;
    foreach (var segment in PlanningWorkPolicy.Candidates(snapshot))
    {
      var load = PlanningWorkPolicy.Resolve(snapshot, segment);
      var savedPlan =
        savedIds is null || savedIds.Contains((load.Id, load.ExecutionLegId))
          ? await ReadPlanAsync(load.Id, truckId, load, ct)
          : null;
      if (savedPlan is null)
        return new(
          truckId,
          load.Id,
          load.LoadNumber,
          null,
          "No saved route is available for this dispatch."
        )
        {
          ExecutionLegId = load.ExecutionLegId,
          AssignmentRevision = load.AssignmentRevision,
        };
      var plan = savedPlan.Plan;
      if (PlanningWorkPolicy.IsCompleted(plan, load))
        continue;
      plan.FuelPlan = null;
      plan.FuelRecommendations = null;
      var truck = (
        serverTelemetry.Current ?? telemetryCache.Latest
      )?.Trucks.FirstOrDefault(x => x.TruckId == truckId);
      var progress =
        truck is null
        || load.ExecutionStatus == "planned" && !plan.FromCurrentPosition
          ? null
          : RouteProgressMeasure.Of(plan, truck, load, savedPlan.Geometry);
      return PlanningWorkPolicy.WithWarnings(
        Result(truckId, load.Id, load.LoadNumber, plan, progress) with
        {
          ExecutionLegId = load.ExecutionLegId,
          AssignmentRevision = load.AssignmentRevision,
        },
        segment
      );
    }
    return NoRemaining(truckId);
  }

  private async Task<PreviewPlan?> ReadPlanAsync(
    Guid dispatchId,
    Guid truckId,
    RouteWorkSnapshot load,
    CancellationToken ct
  )
  {
    var snapshot = await displays.GetAsync(
      dispatchId,
      () =>
        db
          .DispatchRoutePlans.AsNoTracking()
          .SingleOrDefaultAsync(
            x =>
              x.DispatchId == dispatchId
              && x.ExecutionLegId == load.ExecutionLegId,
            ct
          ),
      ct,
      load.ExecutionLegId
    );
    if (snapshot?.Metadata.TruckId != truckId)
      return null;
    var plan = snapshot.ReadPlan();
    if (
      plan.TruckId != truckId
      || plan.DispatchId != dispatchId
      || plan.ExecutionLegId != load.ExecutionLegId
      || load.ExecutionLegId.HasValue
        && plan.AssignmentRevision != load.AssignmentRevision
    )
      return null;
    var profile = await routes.ProfileAsync(truckId, ct);
    if (!RoutePlanInputs.Matches(snapshot.Metadata, load, profile))
      return null;
    plan.Profile = profile;
    plan.InputsChanged = false;
    await routes.AddDisplayReferenceAsync(plan, load, ct);
    return new(plan, snapshot.Geometry);
  }

  private static AutomaticPlanningResult Result(
    Guid truckId,
    Guid dispatchId,
    int loadNumber,
    RoutePlan plan,
    RouteProgress? progress
  ) =>
    new(
      truckId,
      dispatchId,
      loadNumber,
      new(plan.Profile, plan, progress, null, null, true),
      null
    );

  private static AutomaticPlanningResult NoRemaining(Guid truckId) =>
    new(
      truckId,
      null,
      null,
      null,
      "No remaining stops in current or upcoming dispatches."
    );
}
