using Domain.Entities.Fuel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Fuel;

public class FuelDiscountConfiguration : IEntityTypeConfiguration<FuelDiscount>
{
  public void Configure(EntityTypeBuilder<FuelDiscount> builder)
  {
    builder.HasKey(x => x.Id);

    builder.Property(x => x.Currency).HasMaxLength(3);

    builder.Property(x => x.Product).HasMaxLength(50);

    builder
      .HasOne(x => x.FuelStation)
      .WithMany(x => x.FuelDiscounts)
      .HasForeignKey(x => x.FuelStationId)
      .OnDelete(DeleteBehavior.Cascade);

    builder.HasIndex(x => new
    {
      x.FuelStationId,
      x.EffectiveFrom,
      x.EffectiveTo,
    });
  }
}
