using System.Text.Json;
using Application.Caching;
using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Queries;
using Application.Features.Execution.Services;
using Application.Features.Fleet.Queries.GetFleetLocations;
using Application.Features.Fleet.Services;
using Domain.Models.Execution;
using Domain.Models.Routing;
using Domain.Rules;
using Domain.Rules.Routing;
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
  FleetTelemetryCache telemetryCache,
  ICurrentCompany companies
)
{
  // One company's fleet preview, never another's: the key names the company.
  // It was one constant key over a process-wide cache, and the read-cache
  // generations it is checked against are not per company either, so for up
  // to 30 seconds any other carrier's request got this one's trucks, loads
  // and roads.
  private string? CacheKey =>
    companies.Id is { } company ? $"route-preview:fleet:{company:N}" : null;
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
      if (
        CacheKey is { } key
        && json.Length <= 8 * 1024 * 1024
        && generation == Generation()
      )
        cache.Set(
          key,
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
    + $"{reads.Generation(ReadGroups.Board)}:{reads.Generation(ReadGroups.Dispatch)}:"
    + $"{reads.Generation(ReadGroups.Settings)}:{reads.Generation(ReadGroups.RoutePreviews)}:"
    + $"{reads.Generation(ReadGroups.Execution)}";

  private List<AutomaticPlanningResult>? ReadCached(string generation) =>
    CacheKey is { } key
    && cache.TryGetValue<SavedPreview>(key, out var saved)
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
    var seen = inputs.Version(truckId);
    var work = await inputs.ReadAsync(truckId, ct, includeHos: false);
    return work is null
      ? NoRemaining(truckId)
      : await ReadCurrentRowAsync(work, seen, null, ct);
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
    var trucks = rows.Where(x => x.TruckId.HasValue)
      .Select(x => x.TruckId!.Value)
      .Distinct()
      .ToArray();
    var seen = trucks.ToDictionary(x => x, inputs.Version);
    var snapshots = await inputs.ReadManyAsync(trucks, ct, includeHos: false);
    var ids = snapshots
      .Values.Select(x => x.CurrentWork?.DispatchId)
      .OfType<Guid>()
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
        var result = await ReadCurrentRowAsync(
          snapshot,
          seen[snapshot.Itinerary.TruckId],
          saved,
          ct
        );
        if (result.State is not null)
          results.Add(result);
      }
      catch (RoutePlanningException) { }
    }
    return results;
  }

  // The row of the work the inputs chose. If its plan was passed after the
  // capture, the inputs are captured again once; the row never steps on to
  // other work by itself.
  private async Task<AutomaticPlanningResult> ReadCurrentRowAsync(
    TruckPlanningInputs work,
    long seen,
    IReadOnlySet<(Guid DispatchId, Guid? ExecutionLegId)>? savedIds,
    CancellationToken ct
  )
  {
    try
    {
      return await ReadRowAsync(work, savedIds, ct);
    }
    catch (RoutePlanningException ex) when (ex.DependencyChanged)
    {
      var fresh = await inputs.ReadAgainAsync(
        work.Itinerary.TruckId,
        seen,
        ct,
        includeHos: false
      );
      return fresh is null
        ? NoRemaining(work.Itinerary.TruckId)
        : await ReadRowAsync(fresh, null, ct);
    }
  }

  private async Task<AutomaticPlanningResult> ReadRowAsync(
    TruckPlanningInputs work,
    IReadOnlySet<(Guid DispatchId, Guid? ExecutionLegId)>? savedIds,
    CancellationToken ct
  )
  {
    var snapshot = work.Itinerary;
    var truckId = snapshot.TruckId;
    if (work.CurrentSegment is { } segment)
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
      PlanningReadService.RequireStillCurrent(plan, load);
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
      async () =>
        await RoutePlanStorage.LoadAsync(
          db,
          await db
            .DispatchRoutePlans.AsNoTracking()
            .SingleOrDefaultAsync(
              x =>
                x.DispatchId == dispatchId
                && x.ExecutionLegId == load.ExecutionLegId,
              ct
            ),
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
