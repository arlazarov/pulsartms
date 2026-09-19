using Domain.Entities.Shipments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Infrastructure.Persistence.Configurations.Shipments;

public sealed class ShipmentConfiguration : IEntityTypeConfiguration<Shipment>
{
  public void Configure(EntityTypeBuilder<Shipment> builder)
  {
    builder.HasKey(x => x.Id);
    builder.Property(x => x.Revision).IsConcurrencyToken();
    builder
      .HasOne<Load>()
      .WithMany()
      .HasForeignKey(x => x.LoadId)
      .OnDelete(DeleteBehavior.Restrict);
    builder.Property(x => x.BillOfLading).HasMaxLength(200);
    builder.OwnsOne(
      x => x.Shipper,
      party =>
      {
        party.Property(x => x.Name).HasMaxLength(300);
        party.Property(x => x.AddressLine1).HasMaxLength(300);
        party.Property(x => x.AddressLine2).HasMaxLength(300);
        party.Property(x => x.City).HasMaxLength(300);
        party.Property(x => x.Region).HasMaxLength(300);
        party.Property(x => x.Country).HasMaxLength(300);
        party.Property(x => x.PostalCode).HasMaxLength(300);
        party.Property(x => x.ContactName).HasMaxLength(300);
        party.Property(x => x.Phone).HasMaxLength(300);
        party.Property(x => x.Email).HasMaxLength(300);
      }
    );
    builder.Navigation(x => x.Shipper).IsRequired();
    builder.OwnsOne(
      x => x.Consignee,
      party =>
      {
        party.Property(x => x.Name).HasMaxLength(300);
        party.Property(x => x.AddressLine1).HasMaxLength(300);
        party.Property(x => x.AddressLine2).HasMaxLength(300);
        party.Property(x => x.City).HasMaxLength(300);
        party.Property(x => x.Region).HasMaxLength(300);
        party.Property(x => x.Country).HasMaxLength(300);
        party.Property(x => x.PostalCode).HasMaxLength(300);
        party.Property(x => x.ContactName).HasMaxLength(300);
        party.Property(x => x.Phone).HasMaxLength(300);
        party.Property(x => x.Email).HasMaxLength(300);
      }
    );
    builder.Navigation(x => x.Consignee).IsRequired();
    builder.OwnsMany(
      x => x.Commodities,
      rows =>
      {
        rows.ToTable("CustomsCommodities");
        rows.WithOwner().HasForeignKey("ShipmentId");
        rows.HasKey("ShipmentId", nameof(ShipmentCommodity.Id));
        rows.Property(x => x.Id).ValueGeneratedNever();
        rows.Property(x => x.Weight).HasPrecision(18, 3);
        rows.Property(x => x.Description).HasMaxLength(500);
        rows.Property(x => x.PackageType).HasMaxLength(500);
        rows.Property(x => x.WeightUnit).HasMaxLength(500);
        rows.Property(x => x.Marks).HasMaxLength(500);
        rows.Property(x => x.Classification).HasMaxLength(500);
        rows.Property(x => x.OriginCountry).HasMaxLength(500);
      }
    );
  }
}

public sealed class ShipmentSaveReceiptConfiguration
  : IEntityTypeConfiguration<ShipmentSaveReceipt>
{
  public void Configure(EntityTypeBuilder<ShipmentSaveReceipt> builder)
  {
    builder.HasKey(x => x.Id);
    builder.Property(x => x.RequestHash).HasMaxLength(64);
    builder.HasIndex(x => x.AggregateId);
  }
}
