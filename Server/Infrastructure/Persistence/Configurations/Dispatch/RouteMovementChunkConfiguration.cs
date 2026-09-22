using Domain.Entities.Dispatch;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Dispatch;

public sealed class RouteMovementChunkConfiguration
  : IEntityTypeConfiguration<RouteMovementChunk>
{
  public void Configure(EntityTypeBuilder<RouteMovementChunk> b)
  {
    b.HasKey(x => x.Id);
    b.HasOne<DispatchRoutePlan>()
      .WithMany()
      .HasForeignKey(x => x.RoutePlanId)
      .OnDelete(DeleteBehavior.Cascade);
    b.HasIndex(x => new
    {
      x.CompanyId,
      x.TruckId,
      x.To,
    });
  }
}
