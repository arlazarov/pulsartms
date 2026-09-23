using Domain.Entities.Fleet;
using Domain.Entities.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Messaging;

public sealed class DriverMessageConfiguration
  : IEntityTypeConfiguration<DriverMessage>
{
  public void Configure(EntityTypeBuilder<DriverMessage> b)
  {
    b.ToTable("DriverMessages");
    b.HasKey(x => x.Id);
    b.Property(x => x.Channel).HasMaxLength(32).IsRequired();
    b.Property(x => x.Recipient).HasMaxLength(16).IsRequired();
    b.Property(x => x.Text).HasMaxLength(4096).IsRequired();
    b.Property(x => x.VisitKeys).HasMaxLength(4000).IsRequired();
    b.Property(x => x.IdempotencyKey).HasMaxLength(64).IsRequired();
    b.Property(x => x.ProviderMessageId).HasMaxLength(200);
    b.Property(x => x.Status).HasMaxLength(32).IsRequired();
    b.Property(x => x.CreatedBy).HasMaxLength(450);
    // Two presses of the same content race for one attempt number; only
    // one of them reaches the provider.
    b.HasIndex(x => new
      {
        x.CompanyId,
        x.IdempotencyKey,
        x.Attempt,
      })
      .IsUnique();
    b.HasIndex(x => new { x.CompanyId, x.ProviderMessageId }).IsUnique();
    b.HasIndex(x => new { x.TruckId, x.DispatchId });
    b.HasOne<Driver>()
      .WithMany()
      .HasForeignKey(x => x.DriverId)
      .OnDelete(DeleteBehavior.Cascade);
    b.HasOne<Truck>()
      .WithMany()
      .HasForeignKey(x => x.TruckId)
      .OnDelete(DeleteBehavior.Cascade);
  }
}
