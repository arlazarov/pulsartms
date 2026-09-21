using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DispatchEntity = global::Domain.Entities.Dispatch.Dispatch;

namespace Infrastructure.Persistence.Configurations.Dispatch;

public class DispatchConfiguration : IEntityTypeConfiguration<DispatchEntity>
{
  public void Configure(EntityTypeBuilder<DispatchEntity> builder)
  {
    builder.HasKey(x => x.Id);
    builder.Property(x => x.OrderNumber).HasMaxLength(100);
    builder.Property(x => x.Status).HasMaxLength(50);
    builder.Property(x => x.CustomerName).HasMaxLength(200);
    builder.Property(x => x.DriverName).HasMaxLength(200);
    builder.Property(x => x.CarrierName).HasMaxLength(200);
    builder.Property(x => x.TruckNumber).HasMaxLength(50);
    builder.Property(x => x.TrailerNumber).HasMaxLength(50);
    builder.Property(x => x.LoadedMiles).HasPrecision(12, 2);
    builder.Property(x => x.Price).HasPrecision(18, 2);
    builder.Property(x => x.Currency).HasMaxLength(10);
    builder.HasIndex(x => new { x.CompanyId, x.LoadNumber }).IsUnique();
    // The board narrows unfinished work by status and delivery date on every
    // read, and had only the load-number index to work with.
    builder.HasIndex(x => new { x.Status, x.DeliveryDate });
    builder.Property(x => x.PlanningAssignmentRevision).IsConcurrencyToken();
    builder.Property(x => x.RouteChoiceRevision).IsConcurrencyToken();
    builder
      .HasOne(x => x.PlanningTruck)
      .WithMany()
      .HasForeignKey(x => x.PlanningTruckId)
      .OnDelete(DeleteBehavior.Restrict);

    builder
      .HasOne(x => x.Customer)
      .WithMany(x => x.Dispatches)
      .HasForeignKey(x => x.CustomerId)
      .OnDelete(DeleteBehavior.SetNull);

    builder
      .HasOne(x => x.Driver)
      .WithMany()
      .HasForeignKey(x => x.DriverId)
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

    builder
      .HasMany(x => x.Stops)
      .WithOne(x => x.Dispatch)
      .HasForeignKey(x => x.DispatchId)
      .OnDelete(DeleteBehavior.Cascade);
  }
}
