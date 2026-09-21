using Domain.Entities.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Execution;

public sealed class ExecutionSourceReceiptConfiguration
  : IEntityTypeConfiguration<ExecutionSourceReceipt>
{
  public void Configure(EntityTypeBuilder<ExecutionSourceReceipt> builder)
  {
    builder.HasKey(x => x.Id);
    builder.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique();
    builder.Property(x => x.RequestHash).HasMaxLength(64).IsRequired();
    builder.Property(x => x.ResultJson).HasMaxLength(65536).IsRequired();
    builder
      .HasOne<ExecutionLeg>()
      .WithMany()
      .HasForeignKey(x => x.ExecutionLegId)
      .OnDelete(DeleteBehavior.Restrict);
    foreach (var property in builder.Metadata.GetProperties())
      property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
  }
}
