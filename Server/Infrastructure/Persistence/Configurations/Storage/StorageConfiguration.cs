using Domain.Entities.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Storage;

public sealed class StorageConnectionConfiguration
  : IEntityTypeConfiguration<StorageConnection>
{
  public void Configure(EntityTypeBuilder<StorageConnection> b)
  {
    b.ToTable("StorageConnections");
    b.HasKey(x => x.Id);
    b.Property(x => x.Kind).HasMaxLength(40).IsRequired();
    b.Property(x => x.DisplayName).HasMaxLength(80).IsRequired();
    b.Property(x => x.State).HasMaxLength(20).IsRequired();
    b.Property(x => x.Root).HasMaxLength(200);
    b.Property(x => x.RootName).HasMaxLength(200);
    b.Property(x => x.ProtectedSecret).HasMaxLength(16_384);
    b.Property(x => x.LastError).HasMaxLength(200);
    b.Property(x => x.Revision).IsConcurrencyToken();
    // One default per company; a second is refused by the database.
    b.HasIndex(x => x.CompanyId)
      .IsUnique()
      .HasFilter("\"IsDefault\"")
      .HasDatabaseName("IX_StorageConnections_Default");
  }
}

public sealed class StoredFileConfiguration
  : IEntityTypeConfiguration<StoredFile>
{
  public void Configure(EntityTypeBuilder<StoredFile> b)
  {
    b.ToTable("StoredFiles");
    b.HasKey(x => x.Id);
    b.Property(x => x.ObjectKey).HasMaxLength(200).IsRequired();
    b.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
    b.Property(x => x.Name).HasMaxLength(200).IsRequired();
    b.Property(x => x.Folder).HasMaxLength(1000).IsRequired();
    b.Property(x => x.OriginalName).HasMaxLength(200).IsRequired();
    b.HasIndex(x => new { x.ConnectionId, x.Folder });
    b.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
    b.Property(x => x.State).HasMaxLength(20).IsRequired();
    // Uploads not yet completed have no key.
    b.HasIndex(x => new { x.ConnectionId, x.ObjectKey })
      .IsUnique()
      .HasFilter("\"ObjectKey\" <> ''");
    b.HasIndex(x => new { x.State, x.UpdatedAt });
    b.HasOne<StorageConnection>()
      .WithMany()
      .HasForeignKey(x => x.ConnectionId)
      .OnDelete(DeleteBehavior.Restrict);
  }
}

public sealed class ManagedFileBlobConfiguration
  : IEntityTypeConfiguration<ManagedFileBlob>
{
  public void Configure(EntityTypeBuilder<ManagedFileBlob> b)
  {
    b.ToTable("ManagedFileBlobs");
    b.HasKey(x => x.Id);
    b.Property(x => x.Content).IsRequired();
  }
}

public sealed class StorageLayoutConfiguration
  : IEntityTypeConfiguration<StorageLayout>
{
  public void Configure(EntityTypeBuilder<StorageLayout> b)
  {
    b.ToTable("StorageLayouts");
    b.HasKey(x => x.Id);
    b.Property(x => x.LoadsFolder).HasMaxLength(300).IsRequired();
    b.Property(x => x.LoadTemplate).HasMaxLength(200).IsRequired();
    b.Property(x => x.CancelledSuffix).HasMaxLength(40).IsRequired();
    b.Property(x => x.InboxFolder).HasMaxLength(300).IsRequired();
    b.Property(x => x.Revision).IsConcurrencyToken();
    b.HasIndex(x => x.CompanyId).IsUnique();
  }
}
