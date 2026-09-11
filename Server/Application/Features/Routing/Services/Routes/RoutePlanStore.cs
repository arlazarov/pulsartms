using Application.Caching;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Models;
using Application.Features.Synchronization.Services;
using Domain.Entities.Dispatch;
using System.Text.Json;

namespace Application.Features.Routing.Services.Routes;

public sealed class RoutePlanStore(IAppDbContext db, ReadCache reads, TruckPlanningProfileService profiles)
{
  public Task<DispatchRoutePlan?> ReadAsync(Guid dispatchId, CancellationToken ct) =>
    reads.GetAsync($"route:{dispatchId}", "value", () => ReadUncachedAsync(dispatchId, ct));

  public Task<DispatchRoutePlan?> ReadUncachedAsync(Guid dispatchId, CancellationToken ct) =>
    db.DispatchRoutePlans.AsNoTracking().SingleOrDefaultAsync(x => x.DispatchId == dispatchId, ct);

  public Task<DispatchRoutePlan?> ReadForUpdateAsync(Guid dispatchId, CancellationToken ct) =>
    db.DispatchRoutePlans.SingleOrDefaultAsync(x => x.DispatchId == dispatchId, ct);

  public async Task SaveAsync(DispatchRoutePlan entity, RoutePlan plan, CancellationToken ct)
  {
    var json = RoutePlanStorage.Serialize(plan);
    var tracked = db.DispatchRoutePlans.Local.FirstOrDefault(x => x.Id == entity.Id);
    if (tracked is not null) tracked.PlanJson = json;
    else
    {
      var original = entity.PlanJson;
      db.DispatchRoutePlans.Attach(entity);
      entity.PlanJson = json;
      db.Entry(entity).Property(nameof(entity.PlanJson)).OriginalValue = original;
      db.Entry(entity).Property(nameof(entity.PlanJson)).IsModified = true;
    }
    await db.SaveChangesAsync(ct);
    Invalidate(entity.DispatchId);
  }

  internal void Invalidate(Guid dispatchId)
  {
    reads.Invalidate($"route:{dispatchId}");
    reads.Invalidate("route-previews");
  }

  public async Task SaveBuiltAsync(DispatchRoutePlan? entity, RoutePlan plan, string inputHash, CancellationToken ct)
  {
    if (entity is null)
    {
      entity = new() { Id = plan.Id, DispatchId = plan.DispatchId };
      db.DispatchRoutePlans.Add(entity);
    }
    entity.TruckId = plan.TruckId;
    entity.InputHash = inputHash;
    entity.CreatedAt = plan.CalculatedAt;
    await SaveAsync(entity, plan, ct);
  }

  public async Task ClearFuelAsync(Guid dispatchId, CancellationToken ct)
  {
    var entity = await ReadForUpdateAsync(dispatchId, ct);
    if (entity is null) return;
    var plan = ReadPlan(entity);
    plan.FuelPlan = null;
    plan.FuelRecommendations = null;
    await SaveAsync(entity, plan, ct);
  }

  public async Task<FuelPlan> StoreFuelAsync(Guid dispatchId, FuelPlan fuel, CancellationToken ct)
  {
    var entity = await ReadForUpdateAsync(dispatchId, ct) ?? throw new RoutePlanningException("Route not found.");
    var plan = ReadPlan(entity);
    if (plan.Version != fuel.RouteVersion) throw new RoutePlanningException("The route changed while calculating fuel. Recalculate the fuel plan.");
    if (fuel.ProfileSignature != JsonSerializer.Serialize(await profiles.GetAsync(plan.TruckId, ct), RoutePlanningService.Json))
      throw new RoutePlanningException("Fuel settings changed during calculation. Recalculate the fuel plan.");
    plan.FuelPlan = fuel;
    await SaveAsync(entity, plan, ct);
    return fuel;
  }

  public async Task StoreRecommendationsAsync(Guid dispatchId, int version, FuelRecommendations recommendations, CancellationToken ct)
  {
    var entity = await ReadForUpdateAsync(dispatchId, ct) ?? throw new RoutePlanningException("Route not found.");
    var plan = ReadPlan(entity);
    if (plan.Version != version) throw new RoutePlanningException("The route changed. Recommendations will update automatically.");
    if (recommendations.SettingsSignature != PlanningSettingsService.Signature(await profiles.GetAsync(plan.TruckId, ct)))
      throw new RoutePlanningException("Fuel settings changed during calculation. Recommendations will update automatically.");
    plan.FuelRecommendations = recommendations;
    await SaveAsync(entity, plan, ct);
  }

  private static RoutePlan ReadPlan(DispatchRoutePlan entity) =>
    JsonSerializer.Deserialize<RoutePlan>(entity.PlanJson, RoutePlanningService.Json)!;
}
