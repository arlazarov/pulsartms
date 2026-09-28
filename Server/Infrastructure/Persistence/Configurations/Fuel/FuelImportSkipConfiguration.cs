using Domain.Entities.Fuel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Fuel;

public class FuelImportSkipConfiguration
  : IEntityTypeConfiguration<FuelImportSkip>
{
  public void Configure(EntityTypeBuilder<FuelImportSkip> builder)
  {
    builder.HasKey(x => x.Id);
    builder.Property(x => x.GmailMessageId).HasMaxLength(255).IsRequired();
    builder.Property(x => x.Reason).HasMaxLength(32).IsRequired();
    builder.HasIndex(x => new { x.CompanyId, x.GmailMessageId }).IsUnique();
    builder.HasIndex(x => new { x.CompanyId, x.SkippedAt });
  }
}
