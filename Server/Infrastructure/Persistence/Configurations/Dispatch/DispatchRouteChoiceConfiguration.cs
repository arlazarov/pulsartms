using Domain.Entities.Dispatch;
using Domain.Entities.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DispatchEntity = global::Domain.Entities.Dispatch.Dispatch;

namespace Infrastructure.Persistence.Configurations.Dispatch;

public sealed class DispatchRouteChoiceConfiguration
  : IEntityTypeConfiguration<DispatchRouteChoice>
{
  public void Configure(EntityTypeBuilder<DispatchRouteChoice> builder)
  {
    builder.HasKey(x => x.Id);
    builder
      .HasOne<ExecutionLeg>()
      .WithMany()
      .HasForeignKey(x => x.ExecutionLegId)
      .OnDelete(DeleteBehavior.Cascade);
    builder
      .HasIndex(x => x.DispatchId)
      .IsUnique()
      .HasFilter("\"ExecutionLegId\" IS NULL");
    builder
      .HasIndex(x => x.ExecutionLegId)
      .IsUnique()
      .HasFilter("\"ExecutionLegId\" IS NOT NULL");
    builder.Property(x => x.InputHash).HasMaxLength(64);
    builder.Property(x => x.Revision).IsConcurrencyToken();
    builder
      .HasOne<DispatchEntity>()
      .WithMany()
      .HasForeignKey(x => x.DispatchId)
      .OnDelete(DeleteBehavior.Cascade);
  }
}
