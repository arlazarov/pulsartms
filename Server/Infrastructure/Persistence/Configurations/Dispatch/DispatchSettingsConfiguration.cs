using Domain.Entities.Dispatch;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Dispatch;

public sealed class DispatchSettingsConfiguration
  : IEntityTypeConfiguration<DispatchSettings>
{
  public void Configure(EntityTypeBuilder<DispatchSettings> builder)
  {
    builder.HasKey(x => x.Id);
    builder.Property(x => x.LoadNumberPrefix).HasMaxLength(16).IsRequired();
    builder
      .Property(x => x.TemperatureUnit)
      .HasMaxLength(16)
      .HasDefaultValue("both")
      .IsRequired();
    builder
      .Property(x => x.DistanceUnit)
      .HasMaxLength(16)
      .HasDefaultValue("both")
      .IsRequired();
    builder.Property(x => x.Revision).IsConcurrencyToken();
  }
}
