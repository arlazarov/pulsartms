using Domain.Entities;
using Domain.Entities.Dispatch;
using Domain.Entities.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Dispatch;

public sealed class DispatchRoutePreviewConfiguration
  : IEntityTypeConfiguration<DispatchRoutePreview>
{
  public void Configure(EntityTypeBuilder<DispatchRoutePreview> builder)
  {
    builder.HasKey(x => x.Id);
    builder
      .HasOne<ExecutionLeg>()
      .WithMany()
      .HasForeignKey(x => x.ExecutionLegId)
      .OnDelete(DeleteBehavior.Cascade);
    builder.Property(x => x.PreviewId).IsConcurrencyToken();
    builder.HasIndex(x => x.ExpiresAt);
    builder
      .HasOne<User>()
      .WithOne()
      .HasForeignKey<DispatchRoutePreview>(x => x.Id)
      .OnDelete(DeleteBehavior.Cascade);
  }
}
