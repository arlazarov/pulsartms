using Domain.Entities.Dispatch;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Dispatch;

public sealed class RouteRecalculationAttemptConfiguration
  : IEntityTypeConfiguration<RouteRecalculationAttempt>
{
  public void Configure(EntityTypeBuilder<RouteRecalculationAttempt> b)
  {
    b.HasKey(x => x.Id);
    b.HasIndex(x => new { x.TruckId, x.CreatedAt });
  }
}
