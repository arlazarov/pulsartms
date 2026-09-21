using Domain.Entities.Execution;
using Domain.Entities.Fleet;
using Domain.Entities.Mileage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DispatchEntity = Domain.Entities.Dispatch.Dispatch;

namespace Infrastructure.Persistence.Configurations.Mileage;

public sealed class MovementConfiguration : IEntityTypeConfiguration<Movement>
{
  public void Configure(EntityTypeBuilder<Movement> builder)
  {
    builder.HasKey(x => x.Id);
    builder.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique();
    builder.HasIndex(x => new
    {
      x.AllocatedDispatchId,
      x.RecordedAt,
      x.Id,
    });
    builder.HasIndex(x => new { x.TruckId, x.StartedAt });
    builder.HasIndex(x => x.PreviousDispatchId);
    builder.HasIndex(x => x.NextDispatchId);
    builder.HasIndex(x => x.CarriedDispatchId);
    builder.Property(x => x.Revision).IsConcurrencyToken();
    builder.Property(x => x.RequestHash).HasMaxLength(64).IsRequired();
    builder.Property(x => x.Origin).HasMaxLength(32).IsRequired();
    builder.Property(x => x.Purpose).HasMaxLength(32).IsRequired();
    builder.Property(x => x.CargoState).HasMaxLength(16).IsRequired();
    builder.Property(x => x.FromLocation).HasMaxLength(500).IsRequired();
    builder.Property(x => x.ToLocation).HasMaxLength(500).IsRequired();
    builder.Property(x => x.AllocationTarget).HasMaxLength(16).IsRequired();
    builder.Property(x => x.AllocationReason).HasMaxLength(500).IsRequired();
    builder.Property(x => x.PlannedMiles).HasPrecision(18, 3);
    builder.Property(x => x.ActualMiles).HasPrecision(18, 3);
    builder.Property(x => x.PlannedSource).HasMaxLength(64);
    builder.Property(x => x.ActualSource).HasMaxLength(64);
    builder.Property(x => x.PlannedSourceReference).HasMaxLength(300);
    builder.Property(x => x.ActualSourceReference).HasMaxLength(300);
    builder
      .HasOne<Truck>()
      .WithMany()
      .HasForeignKey(x => x.TruckId)
      .OnDelete(DeleteBehavior.Restrict);
    builder
      .HasOne<Driver>()
      .WithMany()
      .HasForeignKey(x => x.DriverId)
      .OnDelete(DeleteBehavior.Restrict);
    builder
      .HasOne<Driver>()
      .WithMany()
      .HasForeignKey(x => x.CoDriverId)
      .OnDelete(DeleteBehavior.Restrict);
    builder
      .HasOne<Trailer>()
      .WithMany()
      .HasForeignKey(x => x.TrailerId)
      .OnDelete(DeleteBehavior.Restrict);
    builder
      .HasOne<ExecutionLeg>()
      .WithMany()
      .HasForeignKey(x => x.ExecutionLegId)
      .OnDelete(DeleteBehavior.Restrict);
    builder
      .HasOne<DispatchEntity>()
      .WithMany()
      .HasForeignKey(x => x.PreviousDispatchId)
      .OnDelete(DeleteBehavior.Restrict);
    builder
      .HasOne<DispatchEntity>()
      .WithMany()
      .HasForeignKey(x => x.NextDispatchId)
      .OnDelete(DeleteBehavior.Restrict);
    builder
      .HasOne<DispatchEntity>()
      .WithMany()
      .HasForeignKey(x => x.CarriedDispatchId)
      .OnDelete(DeleteBehavior.Restrict);
    builder
      .HasOne<DispatchEntity>()
      .WithMany()
      .HasForeignKey(x => x.AllocatedDispatchId)
      .OnDelete(DeleteBehavior.Restrict);
    var immutable = new[]
    {
      nameof(Movement.IdempotencyKey),
      nameof(Movement.RequestHash),
      nameof(Movement.TruckId),
      nameof(Movement.DriverId),
      nameof(Movement.CoDriverId),
      nameof(Movement.TrailerId),
      nameof(Movement.ExecutionLegId),
      nameof(Movement.Origin),
      nameof(Movement.FromVisitId),
      nameof(Movement.ToVisitId),
      nameof(Movement.Purpose),
      nameof(Movement.CargoState),
      nameof(Movement.PreviousDispatchId),
      nameof(Movement.NextDispatchId),
      nameof(Movement.CarriedDispatchId),
      nameof(Movement.FromLocation),
      nameof(Movement.ToLocation),
      nameof(Movement.RecordedAt),
      nameof(Movement.RecordedBy),
    };
    foreach (var property in immutable)
      builder
        .Property(property)
        .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
  }
}

public sealed class MovementDistanceEvidenceConfiguration
  : IEntityTypeConfiguration<MovementDistanceEvidence>
{
  public void Configure(EntityTypeBuilder<MovementDistanceEvidence> builder)
  {
    builder.HasKey(x => x.Id);
    builder.HasIndex(x => new { x.MovementId, x.Revision }).IsUnique();
    builder.Property(x => x.Basis).HasMaxLength(16).IsRequired();
    builder.Property(x => x.Miles).HasPrecision(18, 3);
    builder.Property(x => x.StartOdometerMeters).HasPrecision(21, 3);
    builder.Property(x => x.EndOdometerMeters).HasPrecision(21, 3);
    builder.Property(x => x.Source).HasMaxLength(64).IsRequired();
    builder.Property(x => x.SourceReference).HasMaxLength(300).IsRequired();
    builder.Property(x => x.Reason).HasMaxLength(500).IsRequired();
    builder
      .HasOne<Movement>()
      .WithMany()
      .HasForeignKey(x => x.MovementId)
      .OnDelete(DeleteBehavior.Restrict);
    foreach (var property in builder.Metadata.GetProperties())
      property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
  }
}

public sealed class MovementAllocationEventConfiguration
  : IEntityTypeConfiguration<MovementAllocationEvent>
{
  public void Configure(EntityTypeBuilder<MovementAllocationEvent> builder)
  {
    builder.HasKey(x => x.Id);
    builder.HasIndex(x => new { x.MovementId, x.Revision }).IsUnique();
    builder.Property(x => x.Target).HasMaxLength(16).IsRequired();
    builder.Property(x => x.Reason).HasMaxLength(500).IsRequired();
    builder
      .HasOne<Movement>()
      .WithMany()
      .HasForeignKey(x => x.MovementId)
      .OnDelete(DeleteBehavior.Restrict);
    foreach (var property in builder.Metadata.GetProperties())
      property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
  }
}

public sealed class MileageAllocationPolicyConfiguration
  : IEntityTypeConfiguration<MileageAllocationPolicy>
{
  public void Configure(EntityTypeBuilder<MileageAllocationPolicy> builder)
  {
    builder.HasKey(x => x.Id);
    builder.Property(x => x.Revision).IsConcurrencyToken();
    builder.Property(x => x.YardReturn).HasMaxLength(16).IsRequired();
    builder.Property(x => x.Home).HasMaxLength(16).IsRequired();
    builder.Property(x => x.Maintenance).HasMaxLength(16).IsRequired();
    builder.Property(x => x.Reposition).HasMaxLength(16).IsRequired();
  }
}
