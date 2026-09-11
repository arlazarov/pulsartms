using Domain.Entities.Dispatch;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Dispatch;

public sealed class DeadheadConfiguration : IEntityTypeConfiguration<DispatchDeadhead>
{
  public void Configure(EntityTypeBuilder<DispatchDeadhead> b)
  {
    b.HasKey(x => x.Id);
    b.HasIndex(x => x.DispatchId).IsUnique();
    b.Property(x => x.InputHash).HasMaxLength(64);
    b.Property(x => x.Miles).HasPrecision(18, 3);
    b.Property(x => x.RetryAfter).IsConcurrencyToken();
    b.HasOne<Domain.Entities.Dispatch.Dispatch>().WithMany().HasForeignKey(x => x.DispatchId).OnDelete(DeleteBehavior.Cascade);
  }
}
