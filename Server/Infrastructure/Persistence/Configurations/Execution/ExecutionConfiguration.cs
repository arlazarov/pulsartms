using Domain.Entities.Execution;
using Domain.Entities.Fleet;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DispatchEntity = Domain.Entities.Dispatch.Dispatch;

namespace Infrastructure.Persistence.Configurations.Execution;

public sealed class ExecutionLegConfiguration
  : IEntityTypeConfiguration<ExecutionLeg>
{
  public void Configure(EntityTypeBuilder<ExecutionLeg> builder)
  {
    builder.HasKey(x => x.Id);
    builder
      .HasOne(x => x.Trip)
      .WithMany()
      .HasForeignKey(x => x.TripId)
      .OnDelete(DeleteBehavior.Restrict);
    builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
    builder.Property(x => x.Revision).IsConcurrencyToken();
    builder
      .HasMany(x => x.Stops)
      .WithOne()
      .HasForeignKey(x => x.ExecutionLegId)
      .OnDelete(DeleteBehavior.Cascade);
    builder.Navigation(x => x.Stops).AutoInclude();
    builder
      .Property(x => x.SourceAssignmentSignature)
      .HasMaxLength(64)
      .IsRequired();
    builder.Property(x => x.SourceSignature).HasMaxLength(64).IsRequired();
    builder
      .Property(x => x.SourceObservedSignature)
      .HasMaxLength(64)
      .IsRequired();
    builder.Property(x => x.SourceReviewReason).HasMaxLength(1000);
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
      .HasOne<DispatchSwitchOperation>()
      .WithMany()
      .HasForeignKey(x => x.StartSwitchId)
      .OnDelete(DeleteBehavior.Restrict);
    builder
      .HasOne<DispatchSwitchOperation>()
      .WithMany()
      .HasForeignKey(x => x.EndSwitchId)
      .OnDelete(DeleteBehavior.Restrict);
    builder.HasIndex(x => new { x.TruckId, x.Status });
    builder
      .HasIndex(x => x.TruckId)
      .IsUnique()
      .HasFilter("\"Status\" = 'active'");
    builder
      .HasIndex(x => x.TrailerId)
      .IsUnique()
      .HasFilter("\"Status\" = 'active' AND \"TrailerId\" IS NOT NULL");
  }
}

public sealed class LoadExecutionLegConfiguration
  : IEntityTypeConfiguration<LoadExecutionLeg>
{
  public void Configure(EntityTypeBuilder<LoadExecutionLeg> builder)
  {
    builder.HasKey(x => x.Id);
    builder
      .HasOne<DispatchEntity>()
      .WithMany()
      .HasForeignKey(x => x.DispatchId)
      .OnDelete(DeleteBehavior.Restrict);
    builder
      .HasOne(x => x.ExecutionLeg)
      .WithMany(x => x.Loads)
      .HasForeignKey(x => x.ExecutionLegId)
      .OnDelete(DeleteBehavior.Restrict);
    builder.HasIndex(x => new { x.DispatchId, x.Sequence }).IsUnique();
    builder.HasIndex(x => new { x.DispatchId, x.ExecutionLegId }).IsUnique();
  }
}

public sealed class DispatchSwitchOperationConfiguration
  : IEntityTypeConfiguration<DispatchSwitchOperation>
{
  public void Configure(EntityTypeBuilder<DispatchSwitchOperation> builder)
  {
    builder.HasKey(x => x.Id);
    builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
    builder.Property(x => x.Revision).IsConcurrencyToken();
    builder.Property(x => x.SiteName).HasMaxLength(500).IsRequired();
    builder.Property(x => x.RequestHash).HasMaxLength(64).IsRequired();
    builder.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique();
  }
}

public sealed class TripConfiguration : IEntityTypeConfiguration<Trip>
{
  public void Configure(EntityTypeBuilder<Trip> builder)
  {
    builder.HasKey(x => x.Id);
    builder.Property(x => x.Name).HasMaxLength(500).IsRequired();
    builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
    builder.Property(x => x.Revision).IsConcurrencyToken();
  }
}

public sealed class SwitchParticipantConfiguration
  : IEntityTypeConfiguration<SwitchParticipant>
{
  public void Configure(EntityTypeBuilder<SwitchParticipant> builder)
  {
    builder.HasKey(x => x.Id);
    builder.Property(x => x.TransferKind).HasMaxLength(30).IsRequired();
    builder
      .Property(x => x.OutgoingRestoreJson)
      .HasMaxLength(4194304)
      .IsRequired();
    builder.Property(x => x.Revision).IsConcurrencyToken();
    builder
      .HasOne(x => x.Switch)
      .WithMany(x => x.Participants)
      .HasForeignKey(x => x.SwitchId)
      .OnDelete(DeleteBehavior.Restrict);
    builder
      .HasOne<DispatchEntity>()
      .WithMany()
      .HasForeignKey(x => x.DispatchId)
      .OnDelete(DeleteBehavior.Restrict);
    builder
      .HasOne<ExecutionLeg>()
      .WithMany()
      .HasForeignKey(x => x.OutgoingLegId)
      .OnDelete(DeleteBehavior.Restrict);
    builder
      .HasOne<ExecutionLeg>()
      .WithMany()
      .HasForeignKey(x => x.IncomingLegId)
      .OnDelete(DeleteBehavior.Restrict);
    builder.HasIndex(x => new { x.SwitchId, x.DispatchId }).IsUnique();
    builder
      .HasIndex(x => x.OutgoingLegId)
      .IsUnique()
      .HasFilter("NOT \"IsCancelled\"");
    builder
      .HasIndex(x => x.IncomingLegId)
      .IsUnique()
      .HasFilter("NOT \"IsCancelled\"");
  }
}

public sealed class TrailerCustodyConfiguration
  : IEntityTypeConfiguration<TrailerCustodyInterval>
{
  public void Configure(EntityTypeBuilder<TrailerCustodyInterval> builder)
  {
    builder.HasKey(x => x.Id);
    builder.Property(x => x.Revision).IsConcurrencyToken();
    builder
      .HasOne<Trailer>()
      .WithMany()
      .HasForeignKey(x => x.TrailerId)
      .OnDelete(DeleteBehavior.Restrict);
    builder
      .HasOne<SwitchParticipant>()
      .WithMany()
      .HasForeignKey(x => x.ParticipantId)
      .OnDelete(DeleteBehavior.Restrict);
    builder
      .HasIndex(x => x.TrailerId)
      .IsUnique()
      .HasFilter("\"ReceivedBy\" IS NULL");
    builder.HasIndex(x => x.ParticipantId).IsUnique();
  }
}

public sealed class ExecutionReceiptConfiguration
  : IEntityTypeConfiguration<ExecutionActionReceipt>
{
  public void Configure(EntityTypeBuilder<ExecutionActionReceipt> builder)
  {
    builder.HasKey(x => x.Id);
    builder.Property(x => x.Action).HasMaxLength(30).IsRequired();
    builder.Property(x => x.RequestHash).HasMaxLength(64).IsRequired();
    builder.Property(x => x.ResultJson).HasMaxLength(65536).IsRequired();
    builder.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique();
    builder
      .HasOne<SwitchParticipant>()
      .WithMany()
      .HasForeignKey(x => x.ParticipantId)
      .OnDelete(DeleteBehavior.Restrict);
    builder
      .HasOne<DispatchSwitchOperation>()
      .WithMany()
      .HasForeignKey(x => x.SwitchId)
      .OnDelete(DeleteBehavior.Restrict);
  }
}
