using Domain.Entities.Dispatch;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Dispatch;

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
  public void Configure(EntityTypeBuilder<Customer> builder)
  {
    builder.HasKey(x => x.Id);
    builder.Property(x => x.Name).IsRequired().HasMaxLength(200);
    builder.Property(x => x.NormalizedName).IsRequired().HasMaxLength(200);
    builder.HasIndex(x => x.NormalizedName).IsUnique();
    builder.Property(x => x.ProfileRevision).IsConcurrencyToken();
    builder.Property(x => x.ProfileJson).IsRequired().HasDefaultValue("{}");
  }
}
