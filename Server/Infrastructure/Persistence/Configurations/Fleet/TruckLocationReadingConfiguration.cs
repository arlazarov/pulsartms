using Domain.Entities.Fleet;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Fleet;

public sealed class TruckLocationReadingConfiguration
  : IEntityTypeConfiguration<TruckLocationReading>
{
  public void Configure(EntityTypeBuilder<TruckLocationReading> builder)
  {
    builder.HasKey(x => x.TruckId);
    builder.HasIndex(x => x.ObservedAt);
    builder.Property(x => x.EngineState).HasMaxLength(32);
    builder.Property(x => x.FormattedLocation).HasMaxLength(500);
    builder.Property(x => x.TrailerNumber).HasMaxLength(64);
    builder.Property(x => x.Latitude).HasPrecision(12, 8);
    builder.Property(x => x.Longitude).HasPrecision(12, 8);
    builder.Property(x => x.Speed).HasPrecision(8, 2);
    builder.Property(x => x.Heading).HasPrecision(8, 2);
    builder.Property(x => x.FuelPercent).HasPrecision(6, 2);
    builder.Property(x => x.OutsideTemperatureCelsius).HasPrecision(6, 2);
    builder
      .HasOne<Truck>()
      .WithMany()
      .HasForeignKey(x => x.TruckId)
      .OnDelete(DeleteBehavior.Cascade);
  }
}
