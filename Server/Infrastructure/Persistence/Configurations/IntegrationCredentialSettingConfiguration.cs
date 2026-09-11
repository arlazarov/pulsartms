using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class IntegrationCredentialSettingConfiguration : IEntityTypeConfiguration<IntegrationCredentialSetting>
{
  public void Configure(EntityTypeBuilder<IntegrationCredentialSetting> builder)
  {
    builder.ToTable("IntegrationCredentialSettings");
    builder.HasKey(value => value.Provider);
    builder.Property(value => value.Provider).HasMaxLength(32);
    builder.Property(value => value.ProtectedValues).HasMaxLength(65_536);
    builder.Property(value => value.Revision).IsConcurrencyToken();
  }
}
