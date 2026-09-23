namespace Domain.Entities.Messaging;

// A file carried by a message. For an inbound file the provider's media id
// is kept until the bytes are copied into the company's storage; the copy
// is retried with backoff until the id expires, and the attachment then
// fails visibly. The stored file is quarantined until checked. The
// attachment id is the stored file's id, so a repeated copy is one file.
public sealed class MessageAttachment : BaseEntity, ICompanyOwned
{
  public Guid CompanyId { get; set; }

  public Guid MessageId { get; set; }
  public Guid? StoredFileId { get; set; }
  public string? ProviderMediaId { get; set; }
  public DateTime? MediaExpiresAt { get; set; }
  public string DeclaredType { get; set; } = "";
  public string OriginalName { get; set; } = "";
  public string Caption { get; set; } = "";
  public string State { get; set; } = MessageAttachmentStates.Pending;
  public int Attempts { get; set; }
  public DateTime NextAttemptAt { get; set; }
  public Guid? LeaseToken { get; set; }
  public DateTime? LeaseUntil { get; set; }
  public string? FailureReason { get; set; }
  public DateTime CreatedAt { get; set; }
}

public static class MessageAttachmentStates
{
  public const string Pending = "pending";
  public const string Stored = "stored";
  public const string Failed = "failed";
}
