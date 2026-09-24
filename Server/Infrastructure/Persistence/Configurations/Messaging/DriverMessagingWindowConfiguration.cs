using Domain.Entities.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Messaging;

public sealed class DriverMessagingWindowConfiguration
  : IEntityTypeConfiguration<DriverMessagingWindow>
{
  public void Configure(EntityTypeBuilder<DriverMessagingWindow> b)
  {
    b.ToTable("DriverMessagingWindows");
    b.HasKey(x => x.Id);
    b.Property(x => x.Channel).HasMaxLength(32).IsRequired();
    b.Property(x => x.Phone).HasMaxLength(16).IsRequired();
    b.HasIndex(x => new
      {
        x.CompanyId,
        x.Channel,
        x.Phone,
      })
      .IsUnique();
  }
}
