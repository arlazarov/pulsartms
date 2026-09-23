namespace Domain.Entities.Storage;

// The bytes of a file kept by the PulsR-managed store while that store is
// database-backed. Separate from StoredFile so metadata reads never load
// them.
public sealed class ManagedFileBlob : BaseEntity, ICompanyOwned
{
  public Guid CompanyId { get; set; }

  public byte[] Content { get; set; } = [];
}
