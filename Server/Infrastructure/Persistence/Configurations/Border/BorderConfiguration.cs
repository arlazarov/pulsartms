using Domain.Entities.Border;
using Domain.Entities.Fleet;
using Domain.Entities.Shipments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Border;

public sealed class BorderConfiguration
  : IEntityTypeConfiguration<BorderCrossing>
{
  public void Configure(EntityTypeBuilder<BorderCrossing> builder)
  {
    builder.HasKey(x => x.Id);
    builder.Property(x => x.Revision).IsConcurrencyToken();
    foreach (
      var property in builder
        .Metadata.GetProperties()
        .Where(x => x.ClrType == typeof(string))
    )
      property.SetMaxLength(300);
    builder.OwnsOne(x => x.CarrierAddress, Party);
    builder.Navigation(x => x.CarrierAddress).IsRequired();
    builder.OwnsMany(
      x => x.Shipments,
      rows =>
      {
        rows.ToTable("BorderShipments");
        rows.WithOwner().HasForeignKey("CrossingId");
        rows.HasKey("CrossingId", nameof(BorderShipment.Id));
        rows.Property(x => x.Id).ValueGeneratedNever();
        rows.HasOne<Shipment>()
          .WithMany()
          .HasForeignKey(x => x.ShipmentId)
          .OnDelete(DeleteBehavior.Restrict);
        rows.HasIndex("CrossingId", nameof(BorderShipment.ShipmentId))
          .IsUnique();
        foreach (
          var property in rows
            .OwnedEntityType.GetProperties()
            .Where(x =>
              x.ClrType == typeof(string) && x.Name != "ShipmentSnapshotJson"
            )
        )
          property.SetMaxLength(300);
        rows.OwnsOne(x => x.Importer, Party);
        rows.Navigation(x => x.Importer).IsRequired();
        rows.OwnsOne(x => x.CustomsBroker, Party);
        rows.Navigation(x => x.CustomsBroker).IsRequired();
      }
    );
    builder.OwnsMany(
      x => x.Crew,
      rows =>
      {
        rows.ToTable("BorderCrew");
        rows.WithOwner().HasForeignKey("CrossingId");
        rows.HasKey("CrossingId", nameof(BorderCrew.Id));
        rows.Property(x => x.Id).ValueGeneratedNever();
        rows.Property(x => x.Role).HasMaxLength(20);
        rows.Property(x => x.DisplayName).HasMaxLength(300);
        rows.Property(x => x.ProtectedDetails).HasMaxLength(64000);
        rows.HasOne<Driver>()
          .WithMany()
          .HasForeignKey(x => x.DriverId)
          .OnDelete(DeleteBehavior.Restrict);
      }
    );
    builder.OwnsMany(
      x => x.Equipment,
      rows =>
      {
        rows.ToTable("BorderEquipment");
        rows.WithOwner().HasForeignKey("CrossingId");
        rows.HasKey("CrossingId", nameof(BorderEquipment.Id));
        rows.Property(x => x.Id).ValueGeneratedNever();
        foreach (
          var property in rows
            .OwnedEntityType.GetProperties()
            .Where(x => x.ClrType == typeof(string))
        )
          property.SetMaxLength(300);
        rows.HasOne<Truck>()
          .WithMany()
          .HasForeignKey(x => x.TruckId)
          .OnDelete(DeleteBehavior.Restrict);
        rows.HasOne<Trailer>()
          .WithMany()
          .HasForeignKey(x => x.TrailerId)
          .OnDelete(DeleteBehavior.Restrict);
      }
    );
  }

  private static void Party<TOwner>(
    OwnedNavigationBuilder<TOwner, ShipmentParty> party
  )
    where TOwner : class
  {
    foreach (
      var property in party
        .OwnedEntityType.GetProperties()
        .Where(x => x.ClrType == typeof(string))
    )
      property.SetMaxLength(300);
  }
}

public sealed class BorderReceiptConfiguration
  : IEntityTypeConfiguration<BorderSaveReceipt>
{
  public void Configure(EntityTypeBuilder<BorderSaveReceipt> builder)
  {
    builder.HasKey(x => x.Id);
    builder.Property(x => x.RequestHash).HasMaxLength(64);
    builder.HasIndex(x => x.CrossingId);
  }
}
