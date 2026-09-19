using Application.Caching;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Models;
using Application.Features.Dispatch.Queries;
using Application.Features.Dispatch.Models;
using Application.Features.Synchronization.Services;
using Microsoft.Extensions.Caching.Memory;
using System.Text.Json;

namespace Application.Features.Routing.Services.Routes;

public sealed class RoutePreviewService(IAppDbContext db, Application.Features.Dispatch.Interfaces.IDispatchBoardReader board, ReadCache reads,
  RouteDisplayCache displays, RoutePlanningService routes, IMemoryCache cache)
{
  private const string CacheKey = "route-preview:fleet";
  private static readonly SemaphoreSlim FleetGate = new(1);
  private sealed record SavedPreview(string Generation, byte[] Json);

  public async Task<List<AutomaticPlanningResult>> GetAsync(CancellationToken ct)
  {
    ct.ThrowIfCancellationRequested();
    var generation = Generation();
    if (ReadCached(generation) is { } hit) return hit;
    await FleetGate.WaitAsync(ct);
    try
    {
      generation = Generation();
      if (ReadCached(generation) is { } ready) return ready;
      var result = await LoadAsync(ct);
      var json = JsonSerializer.SerializeToUtf8Bytes(result, RoutePlanningService.Json);
      if (json.Length <= 8 * 1024 * 1024 && generation == Generation())
        cache.Set(CacheKey, new SavedPreview(generation, json), TimeSpan.FromSeconds(30));
      return result;
    }
    finally { FleetGate.Release(); }
  }

  private string Generation() => $"{DateOnly.FromDateTime(DateTime.UtcNow):O}:{reads.Generation("board")}:{reads.Generation("dispatch")}:{reads.Generation("settings")}:{reads.Generation("route-previews")}";

  private List<AutomaticPlanningResult>? ReadCached(string generation) =>
    cache.TryGetValue<SavedPreview>(CacheKey, out var saved) && saved!.Generation == generation
      ? JsonSerializer.Deserialize<List<AutomaticPlanningResult>>(saved.Json, RoutePlanningService.Json) : null;

  public async Task<AutomaticPlanningResult> ForTruckAsync(Guid truckId, CancellationToken ct)
  {
    ct.ThrowIfCancellationRequested();
    var page = await board.ReadAsync(new(TruckId: truckId, IncludeHos: false, IncludeFinancials: false, IncludeEta: false), ct);
    var row = page.Items.FirstOrDefault(x => x.TruckId == truckId);
    return row is null ? NoRemaining(truckId) : await ReadRowAsync(row, id => routes.LoadAsync(id, ct), null, ct);
  }

  private async Task<List<AutomaticPlanningResult>> LoadAsync(CancellationToken ct)
  {
    var rows = new List<Application.Features.Dispatch.Models.TruckDispatchBoardResponse>();
    for (var page = 1; ; page++)
    {
      var boardPage = await board.ReadAsync(new(Page: page, PageSize: 100, IncludeHos: false, IncludeFinancials: false, IncludeEta: false), ct);
      rows.AddRange(boardPage.Items);
      if (!boardPage.HasNextPage) break;
    }
    var ids = rows.SelectMany(x => x.Dispatches).Select(x => x.Id).Distinct().ToArray();
    var saved = (await db.DispatchRoutePlans.AsNoTracking().Where(x => ids.Contains(x.DispatchId))
      .Select(x => x.DispatchId).ToListAsync(ct)).ToHashSet();
    var current = await db.Dispatches.AsNoTracking().Include(x => x.Stops)
      .Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
    var numbers = current.Values.Where(x => !x.TruckId.HasValue && x.Stops.All(s => !s.TruckId.HasValue)
      && !string.IsNullOrWhiteSpace(x.TruckNumber)).Select(x => x.TruckNumber).Distinct().ToArray();
    var fallbackTrucks = numbers.Length == 0 ? new Dictionary<string, Guid>()
      : (await db.Trucks.AsNoTracking().Where(x => numbers.Contains(x.UnitNumber))
        .Select(x => new { x.Id, x.UnitNumber }).ToListAsync(ct))
        .GroupBy(x => x.UnitNumber).ToDictionary(x => x.Key, x => x.First().Id);
    var results = new List<AutomaticPlanningResult>();
    foreach (var row in rows.Where(x => x.TruckId.HasValue))
    {
      try
      {
        var result = await ReadRowAsync(row, id => current.TryGetValue(id, out var load)
          ? routes.ResolveAssignmentAsync(load, ct, fallbackTrucks)
          : throw new RoutePlanningException("Dispatch not found."), saved, ct);
        if (result.State is not null) results.Add(result);
      }
      catch (RoutePlanningException) { }
    }
    return results;
  }

  private async Task<AutomaticPlanningResult> ReadRowAsync(TruckDispatchBoardResponse row,
    Func<Guid, Task<Domain.Entities.Dispatch.Dispatch>> resolve, IReadOnlySet<Guid>? savedIds, CancellationToken ct)
  {
    var truckId = row.TruckId!.Value;
    foreach (var load in row.Dispatches)
    {
      var resolved = await resolve(load.Id);
      if (resolved.TruckId != truckId)
        throw new RoutePlanningException("This load has multiple truck assignments; its route is not available for this truck.");
      var plan = savedIds is null || savedIds.Contains(load.Id) ? await ReadPlanAsync(load.Id, truckId, resolved, ct) : null;
      if (plan is null)
        return new(truckId, load.Id, load.LoadNumber, null, "No saved route is available for this dispatch.");
      if (plan.Tracking.AllStopsPassed) continue;
      plan.FuelPlan = null;
      plan.FuelRecommendations = null;
      return Result(truckId, load.Id, load.LoadNumber, plan);
    }
    return NoRemaining(truckId);
  }

  private async Task<RoutePlan?> ReadPlanAsync(Guid dispatchId, Guid truckId, Domain.Entities.Dispatch.Dispatch load, CancellationToken ct)
  {
    var snapshot = await displays.GetAsync(dispatchId,
      () => db.DispatchRoutePlans.AsNoTracking().SingleOrDefaultAsync(x => x.DispatchId == dispatchId, ct), ct);
    if (snapshot?.Metadata.TruckId != truckId) return null;
    var plan = snapshot.ReadPlan();
    if (plan.TruckId != truckId || plan.DispatchId != dispatchId) return null;
    var profile = await routes.ProfileAsync(truckId, ct);
    if (!RoutePlanningService.MatchesInputs(snapshot.Metadata, load, profile)) return null;
    plan.Profile = profile;
    plan.InputsChanged = false;
    return plan;
  }

  private static AutomaticPlanningResult Result(Guid truckId, Guid dispatchId, int loadNumber, RoutePlan plan) =>
    new(truckId, dispatchId, loadNumber, new(plan.Profile, plan, null, null, null, true), null);

  private static AutomaticPlanningResult NoRemaining(Guid truckId) =>
    new(truckId, null, null, null, "No remaining stops in current or upcoming dispatches.");
}
