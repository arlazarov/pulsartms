using Domain.Entities.Dispatch;
using Domain.Entities.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DispatchEntity = global::Domain.Entities.Dispatch.Dispatch;

namespace Infrastructure.Persistence.Configurations.Dispatch;

public sealed class BaseRouteConfiguration
  : IEntityTypeConfiguration<DispatchBaseRoute>
{
  public void Configure(EntityTypeBuilder<DispatchBaseRoute> b)
  {
    b.HasKey(x => x.Id);
    b.HasOne<ExecutionLeg>()
      .WithMany()
      .HasForeignKey(x => x.ExecutionLegId)
      .OnDelete(DeleteBehavior.Cascade);
    b.HasIndex(x => x.DispatchId)
      .IsUnique()
      .HasFilter("\"ExecutionLegId\" IS NULL");
    b.HasIndex(x => x.ExecutionLegId)
      .IsUnique()
      .HasFilter("\"ExecutionLegId\" IS NOT NULL");
    b.Property(x => x.InputHash).HasMaxLength(64);
    b.HasOne<DispatchEntity>()
      .WithMany()
      .HasForeignKey(x => x.DispatchId)
      .OnDelete(DeleteBehavior.Cascade);
  }
}
