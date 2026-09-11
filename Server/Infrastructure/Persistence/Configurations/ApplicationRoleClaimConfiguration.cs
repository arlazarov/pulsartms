using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class ApplicationRoleClaimConfiguration : IEntityTypeConfiguration<IdentityUserClaim<string>>
{
  public void Configure(EntityTypeBuilder<IdentityUserClaim<string>> builder)
  {
    builder.HasIndex(claim => claim.UserId);
    builder.HasIndex(claim => claim.UserId, "ApplicationRole")
      .HasDatabaseName("UX_AspNetUserClaims_ApplicationRole")
      .HasFilter("\"ClaimType\" = 'amftms:role'")
      .IsUnique();
  }
}
