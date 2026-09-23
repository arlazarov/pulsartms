namespace Domain.Entities.Storage;

// One object in one storage connection, and the only owner of that object.
// Messages and documents reference it; its bytes are never cached here.
//
// Name, type, size and SHA-256 are the upload's fingerprint, fixed when the
// upload is first recorded; the object key is reserved at the provider at
// the same time. Whoever holds UploadToken until UploadLeaseUntil is the
// only one who may upload or complete it.
public sealed class StoredFile : BaseEntity, ICompanyOwned
{
  public Guid CompanyId { get; set; }

  public Guid ConnectionId { get; set; }
  public string ObjectKey { get; set; } = "";
  public string ContentType { get; set; } = "";

  // Readable name and folder path ("Dispatch/Loads/2026.09.21 - 1407") as
  // written; the name the file arrived with is kept apart. Neither is the
  // file's identity.
  public string Name { get; set; } = "";
  public string Folder { get; set; } = "";
  public string OriginalName { get; set; } = "";
  public long Size { get; set; }
  public string Sha256 { get; set; } = "";
  public string State { get; set; } = StoredFileStates.Quarantined;
  public Guid? UploadToken { get; set; }
  public DateTime? UploadLeaseUntil { get; set; }
  public DateTime CreatedAt { get; set; }
  public DateTime UpdatedAt { get; set; }
}

public static class StoredFileStates
{
  // Recorded before the provider is called, so an upload whose outcome was
  // lost is found again by the file id instead of repeated or orphaned.
  public const string Uploading = "uploading";

  // The provider holds no object for an upload that stopped.
  public const string Failed = "failed";

  // Written but not yet checked: never served or filed.
  public const string Quarantined = "quarantined";
  public const string Available = "available";

  // Checked and refused, or the content did not match its fingerprint;
  // kept for review, never served.
  public const string Rejected = "rejected";
  public const string Deleting = "deleting";

  // The row exists but its connection no longer has the object.
  public const string Missing = "missing";
}
