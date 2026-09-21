using Domain.Entities.Dispatch;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Dispatch;

public sealed class DispatchSourceConfiguration
  : IEntityTypeConfiguration<DispatchSourceLink>
{
  public void Configure(EntityTypeBuilder<DispatchSourceLink> builder)
  {
    // The same broker gives two carriers the same external id for
    // different loads; the key is the carrier plus that pair.
    builder.HasKey(x => new
    {
      x.CompanyId,
      x.Provider,
      x.ExternalId,
    });
    builder.Property(x => x.Provider).HasMaxLength(100);
    builder.Property(x => x.ExternalId).HasMaxLength(200);
    builder.Property(x => x.DisplayName).HasMaxLength(100).IsRequired();
    builder.Property(x => x.AssignmentProposalJson).IsRequired();
    builder.Property(x => x.AssignmentSignature).HasMaxLength(64).IsRequired();
    builder.Property(x => x.ExecutionReviewReason).HasMaxLength(1000);
    builder.HasIndex(x => x.DispatchId).IsUnique();
    builder
      .HasOne(x => x.Dispatch)
      .WithOne()
      .HasForeignKey<DispatchSourceLink>(x => x.DispatchId)
      .OnDelete(DeleteBehavior.Restrict);
  }
}

public sealed class DispatchNumberCounterConfiguration
  : IEntityTypeConfiguration<DispatchNumberCounter>
{
  public void Configure(EntityTypeBuilder<DispatchNumberCounter> builder)
  {
    builder.HasKey(x => x.Id);
    builder.Property(x => x.Id).HasMaxLength(40);
    builder.Property(x => x.NextNumber).IsConcurrencyToken();
  }
}
