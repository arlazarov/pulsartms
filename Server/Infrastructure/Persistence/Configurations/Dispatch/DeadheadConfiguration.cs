using Domain.Entities.Dispatch;
using Domain.Entities.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DispatchEntity = global::Domain.Entities.Dispatch.Dispatch;

namespace Infrastructure.Persistence.Configurations.Dispatch;

public sealed class DeadheadConfiguration
  : IEntityTypeConfiguration<DispatchDeadhead>
{
  public void Configure(EntityTypeBuilder<DispatchDeadhead> b)
  {
    b.HasKey(x => x.Id);
    b.HasOne<ExecutionLeg>()
      .WithMany()
      .HasForeignKey(x => x.ExecutionLegId)
      .OnDelete(DeleteBehavior.Cascade);
    b.HasOne<ExecutionLeg>()
      .WithMany()
      .HasForeignKey(x => x.PreviousExecutionLegId)
      .OnDelete(DeleteBehavior.Restrict);
    b.HasIndex(x => x.DispatchId)
      .IsUnique()
      .HasFilter("\"ExecutionLegId\" IS NULL");
    b.HasIndex(x => x.ExecutionLegId)
      .IsUnique()
      .HasFilter("\"ExecutionLegId\" IS NOT NULL");
    b.Property(x => x.InputHash).HasMaxLength(64);
    b.Property(x => x.Miles).HasPrecision(18, 3);
    b.Property(x => x.RetryAfter).IsConcurrencyToken();
    b.HasOne<DispatchEntity>()
      .WithMany()
      .HasForeignKey(x => x.DispatchId)
      .OnDelete(DeleteBehavior.Cascade);
  }
}
