using Domain.Entities.Dispatch;
using Domain.Entities.Execution;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

public partial class AppDbContext
{
  public override int SaveChanges(bool acceptAllChangesOnSuccess)
  {
    BeforeSaving();
    return base.SaveChanges(acceptAllChangesOnSuccess);
  }

  public override Task<int> SaveChangesAsync(
    bool acceptAllChangesOnSuccess,
    CancellationToken cancellationToken = default
  )
  {
    BeforeSaving();
    return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
  }

  private void BeforeSaving()
  {
    ProtectExecutionHistory();
    var removedPlans = ChangeTracker
      .Entries<DispatchRoutePlan>()
      .Where(x => x.State == EntityState.Deleted)
      .Select(x => x.Entity.Id)
      .ToHashSet();
    if (
      ChangeTracker
        .Entries<RouteGeometryChange>()
        .Any(x =>
          x.State == EntityState.Modified
          || x.State == EntityState.Deleted
            && !removedPlans.Contains(x.Entity.RoutePlanId)
        )
    )
      throw new InvalidOperationException("Route changes are immutable.");
    if (
      ChangeTracker
        .Entries<RouteGeometryChunk>()
        .Any(x =>
          x.State == EntityState.Modified
          || x.State == EntityState.Deleted
            && !removedPlans.Contains(x.Entity.RoutePlanId)
        )
    )
      throw new InvalidOperationException("Route chunks are immutable.");
    if (
      ChangeTracker
        .Entries<RouteMovementChunk>()
        .Any(x =>
          x.State == EntityState.Modified
          || x.State == EntityState.Deleted
            && !removedPlans.Contains(x.Entity.RoutePlanId)
        )
    )
      throw new InvalidOperationException("Route movement is immutable.");
    StampNewRowsWithTheCompany();
  }

  private void ProtectExecutionHistory()
  {
    if (
      ChangeTracker
        .Entries<ExecutionLegRevision>()
        .Any(x => x.State is EntityState.Modified or EntityState.Deleted)
    )
      throw new InvalidOperationException(
        "Accepted execution history cannot be changed or removed."
      );
  }

  protected override void OnModelCreating(ModelBuilder modelBuilder)
  {
    base.OnModelCreating(modelBuilder);

    modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    FilterEveryCompanyOwnedTable(modelBuilder);
  }
}
