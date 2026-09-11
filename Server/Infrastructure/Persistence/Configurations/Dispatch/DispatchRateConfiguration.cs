using Domain.Entities.Dispatch;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Dispatch;

public sealed class DispatchRateConfiguration : IEntityTypeConfiguration<DispatchRate>
{
  public void Configure(EntityTypeBuilder<DispatchRate> b)
  {
    b.HasKey(x => x.Id);
    b.HasIndex(x => x.DispatchId).IsUnique();
    b.Property(x => x.Price).HasPrecision(18, 2);
    b.Property(x => x.LoadedMiles).HasPrecision(12, 2);
    b.Property(x => x.EmptyMiles).HasPrecision(18, 3);
    b.Property(x => x.LoadedRatePerMile).HasPrecision(18, 6);
    b.Property(x => x.TotalRatePerMile).HasPrecision(18, 6);
    b.Property(x => x.Currency).HasMaxLength(10);
    b.Property(x => x.ConnectionHash).HasMaxLength(64);
    b.HasOne<Domain.Entities.Dispatch.Dispatch>().WithMany().HasForeignKey(x => x.DispatchId).OnDelete(DeleteBehavior.Cascade);
  }
}
