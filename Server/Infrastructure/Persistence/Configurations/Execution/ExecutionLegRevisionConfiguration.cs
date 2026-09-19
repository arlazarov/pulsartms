using Domain.Entities.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Execution;

public sealed class ExecutionLegRevisionConfiguration
  : IEntityTypeConfiguration<ExecutionLegRevision>
{
  public void Configure(EntityTypeBuilder<ExecutionLegRevision> builder)
  {
    builder.HasKey(x => new { x.ExecutionLegId, x.Revision });
    builder.Property(x => x.Operation).HasMaxLength(40).IsRequired();
    builder.Property(x => x.SnapshotJson).IsRequired();
    builder
      .HasOne<ExecutionLeg>()
      .WithMany()
      .HasForeignKey(x => x.ExecutionLegId)
      .OnDelete(DeleteBehavior.Restrict);
    builder.HasIndex(x => new { x.TruckId, x.RecordedAt });
    builder.HasIndex(x => x.CorrelationId);
  }
}
