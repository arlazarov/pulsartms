using Domain.Entities.Dispatch;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DispatchEntity = Domain.Entities.Dispatch.Dispatch;

namespace Infrastructure.Persistence.Configurations.Dispatch;

public sealed class DispatchDocumentConfiguration
  : IEntityTypeConfiguration<DispatchDocument>
{
  public void Configure(EntityTypeBuilder<DispatchDocument> builder)
  {
    builder.HasKey(x => x.Id);
    builder.HasIndex(x => new { x.DispatchId, x.RecordedAt });
    builder.Property(x => x.ActorName).HasMaxLength(200).IsRequired();
    builder.Property(x => x.Kind).HasMaxLength(16).IsRequired();
    builder.Property(x => x.FileName).HasMaxLength(180).IsRequired();
    builder.Property(x => x.ContentType).HasMaxLength(64).IsRequired();
    builder.Property(x => x.ContentHash).HasMaxLength(64).IsRequired();
    builder.Property(x => x.Content).IsRequired();
    builder
      .HasOne<DispatchEntity>()
      .WithMany()
      .HasForeignKey(x => x.DispatchId)
      .OnDelete(DeleteBehavior.Restrict);
    foreach (var property in builder.Metadata.GetProperties())
      property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
  }
}
