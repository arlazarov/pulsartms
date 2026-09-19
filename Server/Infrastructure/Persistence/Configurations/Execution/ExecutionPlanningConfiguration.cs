using Domain.Entities.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Execution;

public sealed class ExecutionPlanningConfiguration
  : IEntityTypeConfiguration<ExecutionPlanningChange>
{
  public void Configure(EntityTypeBuilder<ExecutionPlanningChange> builder)
  {
    builder.HasKey(x => x.Id);
    builder
      .HasIndex(x => new { x.AvailableAt, x.LeaseUntil })
      .HasFilter("\"CompletedAt\" IS NULL");
    builder.HasIndex(x => x.CompletedAt);
    builder.HasIndex(x => new
    {
      x.ExecutionLegId,
      x.AssignmentRevision,
      x.DispatchId,
    });
  }
}
