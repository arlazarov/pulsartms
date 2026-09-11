using Domain.Entities.Fuel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Fuel;

public class FuelImportSourceConfiguration : IEntityTypeConfiguration<FuelImportSource>
{
  public void Configure(EntityTypeBuilder<FuelImportSource> builder)
  {
    builder.HasKey(x => x.Id);

    builder.Property(x => x.GmailMessageId).HasMaxLength(255).IsRequired();

    builder.Property(x => x.AttachmentName).HasMaxLength(500).IsRequired();

    builder.HasIndex(x => x.GmailMessageId).IsUnique();
  }
}
