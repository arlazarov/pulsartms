using Domain.Entities.Dispatch;
using Domain.Entities.Fleet;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Dispatch;

public class RoutePlanningConfiguration : IEntityTypeConfiguration<DispatchRoutePlan>,
  IEntityTypeConfiguration<TruckPlanningProfile>, IEntityTypeConfiguration<RoutingApiCall>
{
  public void Configure(EntityTypeBuilder<DispatchRoutePlan> b)
  {
    b.HasKey(x => x.Id);
    b.HasIndex(x => x.DispatchId).IsUnique();
    b.Property(x => x.InputHash).HasMaxLength(64);
    b.Property(x => x.PlanJson).IsConcurrencyToken();
    b.HasOne<Domain.Entities.Dispatch.Dispatch>().WithMany().HasForeignKey(x => x.DispatchId).OnDelete(DeleteBehavior.Cascade);
    b.HasOne<Truck>().WithMany().HasForeignKey(x => x.TruckId).OnDelete(DeleteBehavior.Cascade);
  }

  public void Configure(EntityTypeBuilder<TruckPlanningProfile> b)
  {
    b.HasKey(x => x.Id);
    b.HasIndex(x => x.TruckId).IsUnique();
    b.HasOne<Truck>().WithMany().HasForeignKey(x => x.TruckId).OnDelete(DeleteBehavior.Cascade);
  }

  public void Configure(EntityTypeBuilder<RoutingApiCall> b)
  {
    b.HasKey(x => x.Id);
    b.HasIndex(x => new { x.RequestHash, x.CreatedAt });
    b.HasIndex(x => x.CreatedAt);
    b.Property(x => x.RequestHash).HasMaxLength(64);
    b.Property(x => x.Operation).HasMaxLength(30);
  }
}
