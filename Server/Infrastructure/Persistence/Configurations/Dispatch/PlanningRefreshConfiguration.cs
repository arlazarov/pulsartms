using Domain.Entities.Dispatch;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Dispatch;

public sealed class PlanningRefreshConfiguration
  : IEntityTypeConfiguration<PlanningRefreshRequest>
{
  public void Configure(EntityTypeBuilder<PlanningRefreshRequest> builder)
  {
    builder.HasKey(x => x.Id);
    builder.Property(x => x.Id).HasMaxLength(100);
    builder.Property(x => x.InputSignature).HasMaxLength(64).IsRequired();
    builder.HasIndex(x => new { x.AvailableAt, x.LeaseUntil });
    builder.HasIndex(x => x.RequestedAt);
  }
}
