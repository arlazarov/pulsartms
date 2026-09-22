using Domain.Entities.Dispatch;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Dispatch;

public sealed class RouteGeometryChunkConfiguration
  : IEntityTypeConfiguration<RouteGeometryChunk>,
    IEntityTypeConfiguration<RouteGeometryChange>
{
  public void Configure(EntityTypeBuilder<RouteGeometryChunk> b)
  {
    b.HasKey(x => new { x.RoutePlanId, x.Key });
    b.Property(x => x.Key).HasMaxLength(64);
    b.HasOne<DispatchRoutePlan>()
      .WithMany()
      .HasForeignKey(x => x.RoutePlanId)
      .OnDelete(DeleteBehavior.Cascade);
  }

  public void Configure(EntityTypeBuilder<RouteGeometryChange> b)
  {
    b.HasKey(x => new { x.RoutePlanId, x.Revision });
    b.HasOne<DispatchRoutePlan>()
      .WithMany()
      .HasForeignKey(x => x.RoutePlanId)
      .OnDelete(DeleteBehavior.Cascade);
  }
}
