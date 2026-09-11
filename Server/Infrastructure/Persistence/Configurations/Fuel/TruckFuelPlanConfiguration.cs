using Domain.Entities.Fleet;
using Domain.Entities.Fuel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Fuel;

public sealed class TruckFuelPlanConfiguration : IEntityTypeConfiguration<TruckFuelPlan>
{
  public void Configure(EntityTypeBuilder<TruckFuelPlan> b)
  {
    b.ToTable("TruckFuelPlans");
    b.HasKey(x => x.Id);
    b.HasIndex(x => x.TruckId).IsUnique();
    b.HasIndex(x => x.RootDispatchId);
    b.HasOne<Truck>().WithMany().HasForeignKey(x => x.TruckId).OnDelete(DeleteBehavior.Cascade);
    b.HasOne<Domain.Entities.Dispatch.Dispatch>().WithMany().HasForeignKey(x => x.RootDispatchId).OnDelete(DeleteBehavior.Cascade);
  }
}
