using Domain.Entities.Fleet;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Fleet;

public class FleetPlanningSettingsConfiguration
  : IEntityTypeConfiguration<FleetPlanningSettings>
{
  public void Configure(EntityTypeBuilder<FleetPlanningSettings> builder)
  {
    builder.HasKey(x => x.Id);
    builder.Property(x => x.Revision).IsConcurrencyToken();
    builder.Property(x => x.SettingsJson).HasMaxLength(4096);
  }
}
