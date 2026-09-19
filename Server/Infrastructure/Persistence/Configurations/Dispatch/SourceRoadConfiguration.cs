using Domain.Entities.Dispatch;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Dispatch;

public sealed class SourceRoadConfiguration
  : IEntityTypeConfiguration<SourceRoadRequest>
{
  public void Configure(EntityTypeBuilder<SourceRoadRequest> builder)
  {
    builder.HasKey(x => x.DispatchId);
    builder.Property(x => x.InputSignature).HasMaxLength(64).IsRequired();
    builder.Property(x => x.DemandIdentity).HasMaxLength(64).IsRequired();
    builder.HasIndex(x => new
    {
      x.Priority,
      x.AvailableAt,
      x.LeaseUntil,
    });
    builder.HasIndex(x => x.RequestedAt);
  }
}
