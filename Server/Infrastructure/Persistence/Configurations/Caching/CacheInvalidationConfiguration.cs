using Domain.Entities.Caching;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Caching;

public sealed class CacheInvalidationConfiguration
  : IEntityTypeConfiguration<CacheInvalidation>
{
  public void Configure(EntityTypeBuilder<CacheInvalidation> builder)
  {
    builder.HasKey(x => x.Id);

    // Every read of the log is a window over this column, and the pruning
    // delete is the same shape.
    builder.HasIndex(x => x.RecordedAt);
    builder.Property(x => x.GroupKey).HasMaxLength(200).IsRequired();
    builder.Property(x => x.RecordedBy).HasMaxLength(64).IsRequired();
  }
}
