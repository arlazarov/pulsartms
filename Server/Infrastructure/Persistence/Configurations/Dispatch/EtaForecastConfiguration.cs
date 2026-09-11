using Domain.Entities.Dispatch;
using Domain.Entities.Fleet;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Dispatch;

public sealed class EtaForecastConfiguration : IEntityTypeConfiguration<DispatchEtaForecast>
{
  public void Configure(EntityTypeBuilder<DispatchEtaForecast> b)
  {
    b.ToTable("DispatchEtaForecasts");
    b.HasKey(x => x.Id);
    b.HasIndex(x => x.DispatchId).IsUnique();
    b.HasIndex(x => x.TruckId);
    b.HasIndex(x => x.RootDispatchId);
    b.Property(x => x.InputHash).HasMaxLength(64);
    b.Property(x => x.DriverExternalId).HasMaxLength(128);
    b.HasOne<Domain.Entities.Dispatch.Dispatch>().WithMany().HasForeignKey(x => x.DispatchId).OnDelete(DeleteBehavior.Cascade);
    b.HasOne<Domain.Entities.Dispatch.Dispatch>().WithMany().HasForeignKey(x => x.RootDispatchId).OnDelete(DeleteBehavior.Cascade);
    b.HasOne<Truck>().WithMany().HasForeignKey(x => x.TruckId).OnDelete(DeleteBehavior.Cascade);
  }
}
