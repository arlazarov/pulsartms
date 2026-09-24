using Domain.Entities;
using Domain.Entities.DriverGroups;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public class UsersConfiguration : IEntityTypeConfiguration<User>
{
  public void Configure(EntityTypeBuilder<User> builder)
  {
    builder.HasIndex(x => x.IdentityUserId).IsUnique();
    builder.Property(x => x.Theme).HasMaxLength(5).HasDefaultValue("light");
    builder
      .Property(x => x.TemperatureUnit)
      .HasMaxLength(16)
      .HasDefaultValue("both");
    builder
      .Property(x => x.DistanceUnit)
      .HasMaxLength(16)
      .HasDefaultValue("both");
    // A deleted group leaves its owner on all drivers.
    builder
      .HasOne<DriverGroup>()
      .WithMany()
      .HasForeignKey(x => x.SelectedDriverGroupId)
      .OnDelete(DeleteBehavior.SetNull);
  }
}
