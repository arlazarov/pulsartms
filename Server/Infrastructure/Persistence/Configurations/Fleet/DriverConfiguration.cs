using Domain.Entities.Fleet;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Fleet;

public class DriverConfiguration : IEntityTypeConfiguration<Driver>
{
  public void Configure(EntityTypeBuilder<Driver> builder)
  {
    builder.HasKey(x => x.Id);

    builder.Property(x => x.ExternalId).HasMaxLength(100).IsRequired();

    builder.HasIndex(x => new { x.CompanyId, x.ExternalId }).IsUnique();

    builder.Property(x => x.Name).HasMaxLength(200).IsRequired();

    builder.Property(x => x.FuelCard).HasMaxLength(50);
    builder.Property(x => x.ImportedName).HasMaxLength(200);
    builder.Property(x => x.ImportedFuelCard).HasMaxLength(50);
    builder.Property(x => x.ConfiguredBy).HasMaxLength(200);
    builder.Property(x => x.ConfigurationRevision).IsConcurrencyToken();
    builder.Property(x => x.Phone).HasMaxLength(40);
    builder.Property(x => x.ImportedPhone).HasMaxLength(40);
    builder.Property(x => x.Email).HasMaxLength(254);
    builder.Property(x => x.ImportedEmail).HasMaxLength(254);
    builder.Property(x => x.WhatsAppPhone).HasMaxLength(16);
    builder.Property(x => x.ContactChangedBy).HasMaxLength(200);
    builder.Property(x => x.ContactRevision).IsConcurrencyToken();

    builder.HasIndex(x => x.FuelCard);
  }
}
