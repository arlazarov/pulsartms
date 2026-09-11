using Domain.Entities.Fleet;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Fleet;

public class TrailerConfiguration : IEntityTypeConfiguration<Trailer>
{
  public void Configure(EntityTypeBuilder<Trailer> builder)
  {
    builder.HasKey(x => x.Id);

    builder.Property(x => x.ExternalId).HasMaxLength(100).IsRequired();

    builder.HasIndex(x => x.ExternalId).IsUnique();

    builder.Property(x => x.UnitNumber).HasMaxLength(50).IsRequired();

    builder.HasIndex(x => x.UnitNumber).IsUnique();

    builder.Property(x => x.Vin).HasMaxLength(17);
  }
}
