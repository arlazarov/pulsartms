using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class CompanyConfiguration : IEntityTypeConfiguration<Company>
{
  public void Configure(EntityTypeBuilder<Company> builder)
  {
    builder.Property(company => company.Key).HasMaxLength(64).IsRequired();
    builder.Property(company => company.Name).HasMaxLength(200).IsRequired();
    builder.HasIndex(company => company.Key).IsUnique();
  }
}
