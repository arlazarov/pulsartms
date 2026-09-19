using Domain.Entities.Dispatch;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Dispatch;

public sealed class PlanningInputRevisionConfiguration
  : IEntityTypeConfiguration<PlanningInputRevision>
{
  public void Configure(EntityTypeBuilder<PlanningInputRevision> builder)
  {
    builder.HasKey(x => x.TruckId);
    builder.Property(x => x.TruckId).ValueGeneratedNever();
  }
}
