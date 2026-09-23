using Domain.Entities.Fleet;
using Domain.Entities.Fuel;
using Domain.Entities.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DispatchEntity = global::Domain.Entities.Dispatch.Dispatch;

namespace Infrastructure.Persistence.Configurations.Fuel;

public sealed class FuelVisitSendConfiguration
  : IEntityTypeConfiguration<FuelVisitSend>
{
  public void Configure(EntityTypeBuilder<FuelVisitSend> b)
  {
    b.ToTable("FuelVisitSends");
    b.HasKey(x => x.Id);
    b.Property(x => x.Content).HasMaxLength(32).IsRequired();
    b.Property(x => x.Text).HasMaxLength(1000).IsRequired();
    b.Property(x => x.Channel).HasMaxLength(32).IsRequired();
    b.Property(x => x.SentBy).HasMaxLength(450);
    // One confirmation of one content for one visit of one accepted
    // assignment. The same content confirmed again is the same hand-over.
    b.HasIndex(x => new
      {
        x.CompanyId,
        x.TruckId,
        x.ScopeId,
        x.AssignmentRevision,
        x.StationId,
        x.BeforeStopId,
        x.Content,
      })
      .IsUnique();
    b.HasIndex(x => new { x.TruckId, x.DispatchId });
    b.HasOne<Truck>()
      .WithMany()
      .HasForeignKey(x => x.TruckId)
      .OnDelete(DeleteBehavior.Cascade);
    b.HasOne<DriverMessage>()
      .WithMany()
      .HasForeignKey(x => x.MessageId)
      .OnDelete(DeleteBehavior.SetNull);
    b.HasOne<DispatchEntity>()
      .WithMany()
      .HasForeignKey(x => x.DispatchId)
      .OnDelete(DeleteBehavior.Cascade);
  }
}
