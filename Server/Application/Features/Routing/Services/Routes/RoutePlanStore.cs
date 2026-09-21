using System.Text.Json;
using Application.Caching;
using Domain.Entities.Dispatch;
using Domain.Models.Routing;
using Domain.Rules;

namespace Application.Features.Routing.Services.Routes;

public sealed class RoutePlanStore(
  IAppDbContext db,
  ReadCache reads,
  TruckPlanningProfileService profiles
)
{
  public Task<DispatchRoutePlan?> ReadAsync(
    Guid dispatchId,
    CancellationToken ct,
    Guid? executionLegId = null
  ) =>
    reads.GetAsync(
      CacheKey(dispatchId, executionLegId),
      "value",
      () => ReadUncachedAsync(dispatchId, ct, executionLegId)
    );

  public Task<DispatchRoutePlan?> ReadUncachedAsync(
    Guid dispatchId,
    CancellationToken ct,
    Guid? executionLegId = null
  ) =>
    db
      .DispatchRoutePlans.AsNoTracking()
      .SingleOrDefaultAsync(
        x => x.DispatchId == dispatchId && x.ExecutionLegId == executionLegId,
        ct
      );

  public Task<DispatchRoutePlan?> ReadForUpdateAsync(
    Guid dispatchId,
    CancellationToken ct,
    Guid? executionLegId = null
  ) =>
    db.DispatchRoutePlans.SingleOrDefaultAsync(
      x => x.DispatchId == dispatchId && x.ExecutionLegId == executionLegId,
      ct
    );

  public async Task SaveAsync(
    DispatchRoutePlan entity,
    RoutePlan plan,
    CancellationToken ct
  )
  {
    if (
      entity.DispatchId != plan.DispatchId
      || entity.ExecutionLegId != plan.ExecutionLegId
    )
      throw new RoutePlanningException("The route assignment changed.");
    await using var owned =
      entity.ExecutionLegId.HasValue && db.Database.CurrentTransaction is null
        ? await db.Database.BeginTransactionAsync(ct)
        : null;
    if (
      entity.ExecutionLegId is { } legId
      && !await db.LockExecutionLegAsync(legId, plan.AssignmentRevision, ct)
    )
      throw new RoutePlanningException("The route assignment changed.");
    if (
      entity.ExecutionLegId is { } insertedLeg
      && db.Entry(entity).State == EntityState.Added
      && await db
        .DispatchRoutePlans.AsNoTracking()
        .AnyAsync(x => x.ExecutionLegId == insertedLeg, ct)
    )
      throw new RoutePlanningException("The route changed during calculation.");
    var json = RoutePlanStorage.Serialize(plan);
    var tracked = db.DispatchRoutePlans.Local.FirstOrDefault(x =>
      x.Id == entity.Id
    );
    if (tracked is not null)
      tracked.PlanJson = json;
    else
    {
      var original = entity.PlanJson;
      db.DispatchRoutePlans.Attach(entity);
      entity.PlanJson = json;
      db.Entry(entity).Property(nameof(entity.PlanJson)).OriginalValue =
        original;
      db.Entry(entity).Property(nameof(entity.PlanJson)).IsModified = true;
    }
    await db.SaveChangesAsync(ct);
    if (owned is not null)
      await owned.CommitAsync(ct);
    Invalidate(entity.DispatchId, entity.ExecutionLegId);
  }

  public static string CacheKey(Guid dispatchId, Guid? executionLegId) =>
    new PlanningScope(dispatchId, executionLegId).CacheKey;

  internal void Invalidate(Guid dispatchId, Guid? executionLegId = null)
  {
    reads.Invalidate(CacheKey(dispatchId, executionLegId));
    reads.Invalidate("route-previews");
  }

  public async Task SaveBuiltAsync(
    DispatchRoutePlan? entity,
    RoutePlan plan,
    string inputHash,
    CancellationToken ct
  )
  {
    if (entity is null)
    {
      entity = new()
      {
        Id = plan.Id,
        DispatchId = plan.DispatchId,
        ExecutionLegId = plan.ExecutionLegId,
      };
      db.DispatchRoutePlans.Add(entity);
    }
    if (
      entity.DispatchId != plan.DispatchId
      || entity.ExecutionLegId != plan.ExecutionLegId
    )
      throw new RoutePlanningException("The route assignment changed.");
    entity.TruckId = plan.TruckId;
    entity.AssignmentRevision = plan.AssignmentRevision;
    entity.InputHash = inputHash;
    entity.CreatedAt = plan.CalculatedAt;
    await SaveAsync(entity, plan, ct);
  }

  internal async Task<FuelPlan> StoreFuelAsync(
    Guid dispatchId,
    FuelPlan fuel,
    CancellationToken ct,
    Guid? executionLegId = null
  )
  {
    if (db.Database.CurrentTransaction is null)
      throw new InvalidOperationException(
        "Fuel writes require a planning publication transaction."
      );
    var entity =
      await ReadForUpdateAsync(dispatchId, ct, executionLegId)
      ?? throw new RoutePlanningException("Route not found.");
    var plan = ReadPlan(entity);
    if (
      plan.Version != fuel.RouteVersion
      || plan.ExecutionLegId != fuel.ExecutionLegId
      || entity.TruckId != fuel.TruckId
      || plan.TruckId != fuel.TruckId
      || plan.ExecutionLegId.HasValue
        && plan.AssignmentRevision != fuel.AssignmentRevision
    )
      throw new RoutePlanningException(
        "The route changed while calculating fuel. Recalculate the fuel plan."
      );
    if (
      fuel.ProfileSignature
      != JsonSerializer.Serialize(
        await profiles.GetUncachedAsync(plan.TruckId, ct),
        RoutingJson.Options
      )
    )
      throw new RoutePlanningException(
        "Fuel settings changed during calculation. Recalculate the fuel plan."
      );
    plan.FuelRecommendations = null;
    plan.FuelPlan = fuel;
    await SaveAsync(entity, plan, ct);
    return fuel;
  }

  private static RoutePlan ReadPlan(DispatchRoutePlan entity) =>
    JsonSerializer.Deserialize<RoutePlan>(
      entity.PlanJson,
      RoutingJson.Options
    )!;
}
