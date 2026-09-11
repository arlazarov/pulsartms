using Domain.Entities.Fuel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Fuel;

public class FuelTransactionConfiguration : IEntityTypeConfiguration<FuelTransaction>
{
  public void Configure(EntityTypeBuilder<FuelTransaction> builder)
  {
    builder.HasKey(x => x.Id);

    builder.Property(x => x.ExternalTransactionId).HasMaxLength(100);

    builder.Property(x => x.TruckNumber).HasMaxLength(50);

    builder.Property(x => x.DriverName).HasMaxLength(200);

    builder.Property(x => x.Product).HasMaxLength(50);

    builder.Property(x => x.Currency).HasMaxLength(3);

    builder
      .HasOne(x => x.FuelStation)
      .WithMany(x => x.FuelTransactions)
      .HasForeignKey(x => x.FuelStationId)
      .OnDelete(DeleteBehavior.Cascade);

    builder.HasIndex(x => x.ExternalTransactionId);

    builder.HasIndex(x => new { x.FuelStationId, x.TransactionDate });
  }
}
