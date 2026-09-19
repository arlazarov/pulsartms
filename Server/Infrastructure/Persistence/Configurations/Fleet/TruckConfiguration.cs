using Domain.Entities.Fleet;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Fleet;

public class TruckConfiguration : IEntityTypeConfiguration<Truck>
{
  public void Configure(EntityTypeBuilder<Truck> builder)
  {
    builder.HasKey(x => x.Id);
    builder.Property(x => x.ExternalId).HasMaxLength(100).IsRequired();
    builder.HasIndex(x => x.ExternalId).IsUnique();
    builder.Property(x => x.UnitNumber).HasMaxLength(50).IsRequired();
    builder.HasIndex(x => x.UnitNumber).IsUnique();
    builder.Property(x => x.Vin).HasMaxLength(17);
    builder.Property(x => x.ImportedVin).HasMaxLength(17);
    builder.Property(x => x.ConfiguredBy).HasMaxLength(200);
    builder.Property(x => x.ConfigurationRevision).IsConcurrencyToken();
    builder
      .HasOne(x => x.Driver)
      .WithOne()
      .HasForeignKey<Truck>(x => x.DriverId)
      .OnDelete(DeleteBehavior.SetNull);
    builder
      .HasOne(x => x.Trailer)
      .WithOne()
      .HasForeignKey<Truck>(x => x.TrailerId)
      .OnDelete(DeleteBehavior.SetNull);
  }
}
