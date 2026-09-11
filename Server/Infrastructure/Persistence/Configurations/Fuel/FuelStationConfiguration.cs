using Domain.Entities.Fuel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Fuel;

public class FuelStationConfiguration : IEntityTypeConfiguration<FuelStation>
{
  public void Configure(EntityTypeBuilder<FuelStation> builder)
  {
    builder.HasKey(x => x.Id);

    builder.Property(x => x.ExternalId).HasMaxLength(100);

    builder.Property(x => x.Name).HasMaxLength(200);

    builder.Property(x => x.Address).HasMaxLength(300);

    builder.Property(x => x.City).HasMaxLength(150);

    builder.Property(x => x.Region).HasMaxLength(100);

    builder.Property(x => x.PostalCode).HasMaxLength(20);

    builder.Property(x => x.Country).HasMaxLength(2);

    builder.HasIndex(x => x.ExternalId);
  }
}
