using Domain.Entities;
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
  }
}
