using Domain.Entities.Dispatch;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DispatchEntity = Domain.Entities.Dispatch.Dispatch;

namespace Infrastructure.Persistence.Configurations.Dispatch;

public sealed class DispatchWorkspaceConfiguration
  : IEntityTypeConfiguration<DispatchWorkspace>
{
  public void Configure(EntityTypeBuilder<DispatchWorkspace> builder)
  {
    builder.HasKey(x => x.Id);
    builder.Property(x => x.Revision).IsConcurrencyToken();
    builder.Property(x => x.SourceReviewReason).HasMaxLength(1000);
    builder
      .HasOne<DispatchEntity>()
      .WithOne()
      .HasForeignKey<DispatchWorkspace>(x => x.Id)
      .OnDelete(DeleteBehavior.Restrict);
  }
}

public sealed class DispatchWorkspaceRevisionConfiguration
  : IEntityTypeConfiguration<DispatchWorkspaceRevision>
{
  public void Configure(EntityTypeBuilder<DispatchWorkspaceRevision> builder)
  {
    builder.HasKey(x => x.Id);
    builder.HasIndex(x => new { x.DispatchId, x.Revision }).IsUnique();
    builder.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique();
    builder.Property(x => x.RequestHash).HasMaxLength(64);
    builder.Property(x => x.Summary).HasMaxLength(500);
    builder.Property(x => x.ActorName).HasMaxLength(200);
    builder
      .HasOne<DispatchEntity>()
      .WithMany()
      .HasForeignKey(x => x.DispatchId)
      .OnDelete(DeleteBehavior.Restrict);
  }
}
