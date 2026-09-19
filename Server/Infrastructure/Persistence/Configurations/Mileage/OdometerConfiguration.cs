using Domain.Entities.Execution;
using Domain.Entities.Fleet;
using Domain.Entities.Mileage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Mileage;

public sealed class OdometerPositionConfiguration
  : IEntityTypeConfiguration<OdometerPosition>
{
  public void Configure(EntityTypeBuilder<OdometerPosition> builder)
  {
    builder.HasKey(x => x.Id);
    builder.HasIndex(x => x.TruckId).IsUnique();
    builder.Property(x => x.ExternalTruckId).HasMaxLength(100).IsRequired();
    builder.Property(x => x.Revision).IsConcurrencyToken();
    builder.Property(x => x.Meters).HasPrecision(21, 3);
    builder
      .HasOne<Truck>()
      .WithMany()
      .HasForeignKey(x => x.TruckId)
      .OnDelete(DeleteBehavior.Restrict);
  }
}

public sealed class OdometerCaptureCheckpointConfiguration
  : IEntityTypeConfiguration<OdometerCaptureCheckpoint>
{
  public void Configure(EntityTypeBuilder<OdometerCaptureCheckpoint> builder)
  {
    builder.HasKey(x => x.Id);
    builder.Property(x => x.Cursor).HasMaxLength(4000);
    builder.Property(x => x.Revision).IsConcurrencyToken();
  }
}

public sealed class MileageCaptureGapConfiguration
  : IEntityTypeConfiguration<MileageCaptureGap>
{
  public void Configure(EntityTypeBuilder<MileageCaptureGap> builder)
  {
    builder.HasKey(x => x.Id);
    builder.HasIndex(x => new { x.TruckId, x.EndedAt });
    builder.HasIndex(x => new { x.ExecutionLegId, x.EndedAt });
    builder.Property(x => x.Reason).HasMaxLength(100).IsRequired();
    builder
      .HasOne<Truck>()
      .WithMany()
      .HasForeignKey(x => x.TruckId)
      .OnDelete(DeleteBehavior.Restrict);
    builder
      .HasOne<ExecutionLeg>()
      .WithMany()
      .HasForeignKey(x => x.ExecutionLegId)
      .OnDelete(DeleteBehavior.Restrict);
    foreach (var property in builder.Metadata.GetProperties())
      property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
  }
}

public sealed class OdometerIntervalConfiguration
  : IEntityTypeConfiguration<OdometerInterval>
{
  public void Configure(EntityTypeBuilder<OdometerInterval> builder)
  {
    builder.HasKey(x => x.Id);
    builder.HasIndex(x => new
    {
      x.Status,
      x.CheckedAt,
      x.EndedAt,
    });
    builder
      .HasIndex(x => new
      {
        x.TruckId,
        x.StartedAt,
        x.EndedAt,
      })
      .IsUnique();
    builder.Property(x => x.ExternalTruckId).HasMaxLength(100).IsRequired();
    builder.Property(x => x.Status).HasMaxLength(16).IsConcurrencyToken();
    builder.Property(x => x.StartMeters).HasPrecision(21, 3);
    builder.Property(x => x.EndMeters).HasPrecision(21, 3);
    builder
      .HasOne<Truck>()
      .WithMany()
      .HasForeignKey(x => x.TruckId)
      .OnDelete(DeleteBehavior.Restrict);
    foreach (
      var property in builder
        .Metadata.GetProperties()
        .Where(x =>
          x.Name
            is not nameof(OdometerInterval.Status)
              and not nameof(OdometerInterval.CheckedAt)
              and not nameof(OdometerInterval.MovementId)
              and not nameof(OdometerInterval.GapId)
        )
    )
      property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
  }
}
