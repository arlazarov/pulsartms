using Domain.Entities.Costs;
using Domain.Entities.Fleet;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DispatchEntity = Domain.Entities.Dispatch.Dispatch;

namespace Infrastructure.Persistence.Configurations.Costs;

public sealed class ExpenseConfiguration : IEntityTypeConfiguration<Expense>
{
  public void Configure(EntityTypeBuilder<Expense> builder)
  {
    builder.HasKey(x => x.Id);
    builder.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique();
    builder.HasIndex(x => new { x.Kind, x.OccurredAt });
    builder.HasIndex(x => new { x.TruckId, x.OccurredAt });
    builder.HasIndex(x => x.ExecutionLegId);
    builder.Property(x => x.Revision).IsConcurrencyToken();
    builder.Property(x => x.Kind).HasMaxLength(32).IsRequired();
    builder.Property(x => x.Currency).HasMaxLength(8).IsRequired();
    builder.Property(x => x.Location).HasMaxLength(500).IsRequired();
    builder.Property(x => x.QuantityUnit).HasMaxLength(16).IsRequired();
    builder.Property(x => x.SourceTruckName).HasMaxLength(100).IsRequired();
    builder.Property(x => x.SourceDriverName).HasMaxLength(200).IsRequired();
    builder.Property(x => x.Source).HasMaxLength(64).IsRequired();
    builder.Property(x => x.SourceReference).HasMaxLength(300).IsRequired();
    // Money keeps more scale than a display value; a unit price and a payout
    // round differently and each states its own precision at its operation.
    builder.Property(x => x.Amount).HasPrecision(18, 4);
    builder.Property(x => x.Quantity).HasPrecision(18, 3);
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
      .HasOne<Trailer>()
      .WithMany()
      .HasForeignKey(x => x.TrailerId)
      .OnDelete(DeleteBehavior.Restrict);
  }
}

public sealed class ExpenseAttributionConfiguration
  : IEntityTypeConfiguration<ExpenseAttribution>
{
  public void Configure(EntityTypeBuilder<ExpenseAttribution> builder)
  {
    builder.HasKey(x => x.Id);
    // A load bears at most one share of a given expense.
    builder.HasIndex(x => new { x.ExpenseId, x.DispatchId }).IsUnique();
    builder.HasIndex(x => x.DispatchId);
    builder.Property(x => x.Revision).IsConcurrencyToken();
    builder.Property(x => x.Basis).HasMaxLength(32).IsRequired();
    builder.Property(x => x.Reason).HasMaxLength(500).IsRequired();
    builder.Property(x => x.Amount).HasPrecision(18, 4);
    builder
      .HasOne<Expense>()
      .WithMany()
      .HasForeignKey(x => x.ExpenseId)
      .OnDelete(DeleteBehavior.Cascade);
    builder
      .HasOne<DispatchEntity>()
      .WithMany()
      .HasForeignKey(x => x.DispatchId)
      .OnDelete(DeleteBehavior.Restrict);
  }
}

public sealed class ExpenseAttributionEventConfiguration
  : IEntityTypeConfiguration<ExpenseAttributionEvent>
{
  public void Configure(EntityTypeBuilder<ExpenseAttributionEvent> builder)
  {
    builder.HasKey(x => x.Id);
    builder.HasIndex(x => new { x.ExpenseId, x.RecordedAt });
    builder.Property(x => x.Basis).HasMaxLength(32).IsRequired();
    builder.Property(x => x.Reason).HasMaxLength(500).IsRequired();
    builder.Property(x => x.PreviousAmount).HasPrecision(18, 4);
    builder.Property(x => x.Amount).HasPrecision(18, 4);
    builder
      .HasOne<Expense>()
      .WithMany()
      .HasForeignKey(x => x.ExpenseId)
      .OnDelete(DeleteBehavior.Cascade);
  }
}
