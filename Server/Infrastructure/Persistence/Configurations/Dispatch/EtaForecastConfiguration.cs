using Domain.Entities.Dispatch;
using Domain.Entities.Execution;
using Domain.Entities.Fleet;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DispatchEntity = global::Domain.Entities.Dispatch.Dispatch;

namespace Infrastructure.Persistence.Configurations.Dispatch;

public sealed class EtaForecastConfiguration
  : IEntityTypeConfiguration<DispatchEtaForecast>
{
  public void Configure(EntityTypeBuilder<DispatchEtaForecast> b)
  {
    b.ToTable("DispatchEtaForecasts");
    b.HasKey(x => x.Id);
    b.HasOne<ExecutionLeg>()
      .WithMany()
      .HasForeignKey(x => x.ExecutionLegId)
      .OnDelete(DeleteBehavior.Cascade);
    b.HasOne<ExecutionLeg>()
      .WithMany()
      .HasForeignKey(x => x.RootExecutionLegId)
      .OnDelete(DeleteBehavior.Restrict);
    b.HasIndex(x => x.DispatchId)
      .IsUnique()
      .HasFilter("\"ExecutionLegId\" IS NULL");
    b.HasIndex(x => x.ExecutionLegId)
      .IsUnique()
      .HasFilter("\"ExecutionLegId\" IS NOT NULL");
    b.HasIndex(x => x.TruckId);
    b.HasIndex(x => x.RootDispatchId);
    b.Property(x => x.InputHash).HasMaxLength(64);
    b.Property(x => x.DriverExternalId).HasMaxLength(128);
    b.HasOne<DispatchEntity>()
      .WithMany()
      .HasForeignKey(x => x.DispatchId)
      .OnDelete(DeleteBehavior.Cascade);
    b.HasOne<DispatchEntity>()
      .WithMany()
      .HasForeignKey(x => x.RootDispatchId)
      .OnDelete(DeleteBehavior.Cascade);
    b.HasOne<Truck>()
      .WithMany()
      .HasForeignKey(x => x.TruckId)
      .OnDelete(DeleteBehavior.Cascade);
  }
}
