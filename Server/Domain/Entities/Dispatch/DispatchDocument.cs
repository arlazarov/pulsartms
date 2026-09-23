namespace Domain.Entities.Dispatch;

public sealed class DispatchDocument : ICompanyOwned
{
  public Guid CompanyId { get; set; }

  public Guid Id { get; set; }
  public Guid DispatchId { get; set; }
  public string Kind { get; set; } = "other";
  public string FileName { get; set; } = "";
  public string ContentType { get; set; } = "";
  public byte[] Content { get; set; } = [];
  public int Length { get; set; }
  public string ContentHash { get; set; } = "";
  public DateTime RecordedAt { get; set; }
  public Guid RecordedBy { get; set; }
  public string ActorName { get; set; } = "";

  // A file a driver sent, filed by a dispatcher: the document refers to
  // the stored file rather than copying its bytes, so Content is empty,
  // and names the message attachment it came from. A load holds a given
  // attachment once.
  public Guid? StoredFileId { get; set; }
  public Guid? SourceAttachmentId { get; set; }
}
