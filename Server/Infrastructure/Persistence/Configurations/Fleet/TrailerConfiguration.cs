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
    builder.Property(x => x.Source).HasMaxLength(32);

    // A trailer first seen by number on a load has no external id yet, and
    // several may wait for one; an id, once given, names one trailer.
    builder
      .HasIndex(x => new { x.CompanyId, x.ExternalId })
      .IsUnique()
      .HasFilter("\"ExternalId\" <> ''");

    builder.Property(x => x.UnitNumber).HasMaxLength(50).IsRequired();

    builder.HasIndex(x => new { x.CompanyId, x.UnitNumber }).IsUnique();

    builder.Property(x => x.Vin).HasMaxLength(17);
    builder.Property(x => x.ImportedVin).HasMaxLength(17);
    builder.Property(x => x.ConfiguredBy).HasMaxLength(200);
    builder.Property(x => x.ConfigurationRevision).IsConcurrencyToken();
  }
}
