using Domain.Entities.Fleet;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Fleet;

public sealed class DriverHosReadingConfiguration
  : IEntityTypeConfiguration<DriverHosReading>
{
  public void Configure(EntityTypeBuilder<DriverHosReading> builder)
  {
    // A provider's driver id is unique only within the carrier's account
    // (audit F28).
    builder.HasKey(x => new { x.CompanyId, x.DriverExternalId });
    builder.Property(x => x.DriverExternalId).HasMaxLength(200);
    builder.Property(x => x.CurrentDutyStatus).HasMaxLength(32);
  }
}
