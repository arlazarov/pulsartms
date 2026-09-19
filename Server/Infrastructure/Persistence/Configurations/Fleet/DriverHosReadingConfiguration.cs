using Domain.Entities.Fleet;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Fleet;

public sealed class DriverHosReadingConfiguration
  : IEntityTypeConfiguration<DriverHosReading>
{
  public void Configure(EntityTypeBuilder<DriverHosReading> builder)
  {
    builder.HasKey(x => x.DriverExternalId);
    builder.Property(x => x.DriverExternalId).HasMaxLength(200);
    builder.Property(x => x.CurrentDutyStatus).HasMaxLength(32);
  }
}
