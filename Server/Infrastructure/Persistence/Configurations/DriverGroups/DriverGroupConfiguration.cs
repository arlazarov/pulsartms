using Domain.Entities;
using Domain.Entities.DriverGroups;
using Domain.Entities.Fleet;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.DriverGroups;

public sealed class DriverGroupConfiguration
  : IEntityTypeConfiguration<DriverGroup>
{
  public void Configure(EntityTypeBuilder<DriverGroup> b)
  {
    b.ToTable("DriverGroups");
    b.HasKey(x => x.Id);
    b.Property(x => x.Name).HasMaxLength(60).IsRequired();
    b.Property(x => x.Revision).IsConcurrencyToken();
    // An owner's groups have distinct names; another dispatcher's may
    // repeat them.
    b.HasIndex(x => new
      {
        x.CompanyId,
        x.OwnerUserId,
        x.Name,
      })
      .IsUnique();
    b.HasOne<User>()
      .WithMany()
      .HasForeignKey(x => x.OwnerUserId)
      .OnDelete(DeleteBehavior.Cascade);
  }
}

public sealed class DriverGroupMemberConfiguration
  : IEntityTypeConfiguration<DriverGroupMember>
{
  public void Configure(EntityTypeBuilder<DriverGroupMember> b)
  {
    b.ToTable("DriverGroupMembers");
    b.HasKey(x => x.Id);
    b.HasIndex(x => new { x.GroupId, x.DriverId }).IsUnique();
    b.HasIndex(x => x.DriverId);
    // Removing a group removes its membership rows only; removing a
    // driver takes them out of every group.
    b.HasOne<DriverGroup>()
      .WithMany()
      .HasForeignKey(x => x.GroupId)
      .OnDelete(DeleteBehavior.Cascade);
    b.HasOne<Driver>()
      .WithMany()
      .HasForeignKey(x => x.DriverId)
      .OnDelete(DeleteBehavior.Cascade);
  }
}
