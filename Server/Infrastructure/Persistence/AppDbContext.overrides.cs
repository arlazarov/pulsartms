using Domain.Entities.Execution;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

public partial class AppDbContext
{
  public override int SaveChanges(bool acceptAllChangesOnSuccess)
  {
    ProtectExecutionHistory();
    return base.SaveChanges(acceptAllChangesOnSuccess);
  }

  public override Task<int> SaveChangesAsync(
    bool acceptAllChangesOnSuccess,
    CancellationToken cancellationToken = default
  )
  {
    ProtectExecutionHistory();
    return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
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
  }
}
