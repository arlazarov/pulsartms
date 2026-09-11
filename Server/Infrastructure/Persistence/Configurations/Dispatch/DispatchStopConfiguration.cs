using Domain.Entities.Dispatch;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Dispatch;

public class DispatchStopConfiguration : IEntityTypeConfiguration<DispatchStop>
{
  public void Configure(EntityTypeBuilder<DispatchStop> builder)
  {
    builder.HasKey(x => x.Id);
    builder.Property(x => x.Job).HasMaxLength(50);
    builder.Property(x => x.Name).HasMaxLength(300);
    builder.Property(x => x.Address).HasMaxLength(500);
    builder.Property(x => x.City).HasMaxLength(150);
    builder.Property(x => x.Province).HasMaxLength(100);
    builder.Property(x => x.Country).HasMaxLength(100);
    builder.Property(x => x.ZipCode).HasMaxLength(50);
    builder.Property(x => x.SourceAddressJson).IsConcurrencyToken();
    builder.Property(x => x.DriverName).HasMaxLength(200);
    builder.Property(x => x.CoDriverName).HasMaxLength(200);
    builder.Property(x => x.CarrierName).HasMaxLength(200);
    builder.Property(x => x.TruckNumber).HasMaxLength(50);
    builder.Property(x => x.TrailerNumber).HasMaxLength(50);
    builder.Property(x => x.Commodity).HasColumnType("text");
    builder.Property(x => x.StopNo).HasMaxLength(100);
    builder.Property(x => x.WeightUnit).HasMaxLength(20);
    builder.Property(x => x.Temperature).HasMaxLength(50);
    builder.Property(x => x.TemperatureUnit).HasMaxLength(20);
    builder.Property(x => x.Latitude).HasPrecision(10, 7);
    builder.Property(x => x.Longitude).HasPrecision(10, 7);
    builder.Property(x => x.Weight).HasPrecision(18, 2);
    builder.Property(x => x.Pieces).HasPrecision(18, 2);
    builder.Property(x => x.Pallets).HasPrecision(18, 2);

    builder
      .HasOne(x => x.Driver)
      .WithMany()
      .HasForeignKey(x => x.DriverId)
      .OnDelete(DeleteBehavior.SetNull);

    builder
      .HasOne(x => x.CoDriver)
      .WithMany()
      .HasForeignKey(x => x.CoDriverId)
      .OnDelete(DeleteBehavior.SetNull);

    builder
      .HasOne(x => x.Truck)
      .WithMany()
      .HasForeignKey(x => x.TruckId)
      .OnDelete(DeleteBehavior.SetNull);

    builder
      .HasOne(x => x.Trailer)
      .WithMany()
      .HasForeignKey(x => x.TrailerId)
      .OnDelete(DeleteBehavior.SetNull);

    builder.HasIndex(x => x.DispatchId);
    builder.HasIndex(x => new { x.DispatchId, x.Sequence });
  }
}
