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
}
