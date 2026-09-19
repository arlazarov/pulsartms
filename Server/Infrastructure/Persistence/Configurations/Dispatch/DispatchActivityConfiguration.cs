using Domain.Entities.Dispatch;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DispatchEntity = Domain.Entities.Dispatch.Dispatch;

namespace Infrastructure.Persistence.Configurations.Dispatch;

public sealed class DispatchActivityConfiguration
  : IEntityTypeConfiguration<DispatchActivityThread>,
    IEntityTypeConfiguration<DispatchActivityEntry>
{
  public void Configure(EntityTypeBuilder<DispatchActivityThread> builder)
  {
    builder.HasKey(x => x.Id);
    builder.Property(x => x.Revision).IsConcurrencyToken();
    builder
      .HasOne<DispatchEntity>()
      .WithOne()
      .HasForeignKey<DispatchActivityThread>(x => x.Id)
      .OnDelete(DeleteBehavior.Restrict);
  }

  public void Configure(EntityTypeBuilder<DispatchActivityEntry> builder)
  {
    builder.HasKey(x => x.Id);
    builder.Property(x => x.Revision).IsConcurrencyToken();
    builder.Property(x => x.Kind).HasMaxLength(30);
    builder.Property(x => x.Text).HasMaxLength(4000);
    builder.Property(x => x.StopLabel).HasMaxLength(1000);
    builder.Property(x => x.DriverName).HasMaxLength(200);
    builder.Property(x => x.ActorName).HasMaxLength(200);
    builder.Property(x => x.ResolvedByName).HasMaxLength(200);
    builder
      .HasOne<DispatchActivityThread>()
      .WithMany()
      .HasForeignKey(x => x.DispatchId)
      .OnDelete(DeleteBehavior.Restrict);
    builder.HasIndex(x => new { x.DispatchId, x.CreatedRevision }).IsUnique();
    builder.HasIndex(x => new { x.DispatchId, x.AddOperationId }).IsUnique();
    builder.HasIndex(x => new
    {
      x.DispatchId,
      x.NeedsAttention,
      x.ResolvedAt,
      x.CreatedRevision,
    });
    foreach (var property in builder.Metadata.GetProperties())
      if (
        property.Name
        is not (
          nameof(DispatchActivityEntry.Revision)
          or nameof(DispatchActivityEntry.ResolveOperationId)
          or nameof(DispatchActivityEntry.ResolvedBy)
          or nameof(DispatchActivityEntry.ResolvedByName)
          or nameof(DispatchActivityEntry.ResolvedAt)
        )
      )
        property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
  }
}
