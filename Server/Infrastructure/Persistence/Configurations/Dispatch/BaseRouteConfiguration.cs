using Domain.Entities.Dispatch;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Dispatch;

public sealed class BaseRouteConfiguration : IEntityTypeConfiguration<DispatchBaseRoute>
{
  public void Configure(EntityTypeBuilder<DispatchBaseRoute> b)
  {
    b.HasKey(x => x.Id);
    b.HasIndex(x => x.DispatchId).IsUnique();
    b.Property(x => x.InputHash).HasMaxLength(64);
    b.HasOne<Domain.Entities.Dispatch.Dispatch>().WithMany().HasForeignKey(x => x.DispatchId).OnDelete(DeleteBehavior.Cascade);
  }
}
