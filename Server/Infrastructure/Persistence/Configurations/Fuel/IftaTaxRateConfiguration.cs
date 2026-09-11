using Domain.Entities.Fuel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Fuel;

public class IftaTaxRateConfiguration : IEntityTypeConfiguration<IftaTaxRate>
{
  public void Configure(EntityTypeBuilder<IftaTaxRate> builder)
  {
    builder.HasKey(x => x.Id);
    builder.Property(x => x.Jurisdiction).HasMaxLength(2).IsRequired();
    builder.Property(x => x.FuelType).HasMaxLength(50).IsRequired();
    builder.Property(x => x.Rate).HasPrecision(10, 4);
    builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
    builder.Property(x => x.Unit).HasMaxLength(3).IsRequired();

    builder
      .HasIndex(x => new
      {
        x.Jurisdiction,
        x.FuelType,
        x.EffectiveFrom,
      })
      .IsUnique();
  }
}
