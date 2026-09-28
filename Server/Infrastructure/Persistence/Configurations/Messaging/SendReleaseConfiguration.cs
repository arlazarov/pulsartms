using Domain.Entities.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Messaging;

public sealed class SendReleaseConfiguration
  : IEntityTypeConfiguration<SendRelease>
{
  public void Configure(EntityTypeBuilder<SendRelease> b)
  {
    b.HasKey(x => x.Id);
    b.Property(x => x.Revision).HasMaxLength(128).IsRequired();
    b.Property(x => x.ReleasedBy).HasMaxLength(450).IsRequired();
    b.HasIndex(x => x.Revision).IsUnique();
  }
}
