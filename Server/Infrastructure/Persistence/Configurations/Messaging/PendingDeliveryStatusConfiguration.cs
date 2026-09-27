using Domain.Entities.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Messaging;

public sealed class PendingDeliveryStatusConfiguration
  : IEntityTypeConfiguration<PendingDeliveryStatus>
{
  public void Configure(EntityTypeBuilder<PendingDeliveryStatus> b)
  {
    b.HasKey(x => x.Id);
    b.Property(x => x.Channel).HasMaxLength(32).IsRequired();
    b.Property(x => x.BusinessNumberId).HasMaxLength(64).IsRequired();
    b.Property(x => x.ProviderMessageId).HasMaxLength(200).IsRequired();
    b.Property(x => x.Status).HasMaxLength(32).IsRequired();
    // The same status for the same message is kept once, however often the
    // provider repeats it.
    b.HasIndex(x => new
      {
        x.CompanyId,
        x.Channel,
        x.BusinessNumberId,
        x.ProviderMessageId,
        x.Status,
      })
      .IsUnique();
    b.HasIndex(x => new { x.CompanyId, x.ReceivedAt });
  }
}
