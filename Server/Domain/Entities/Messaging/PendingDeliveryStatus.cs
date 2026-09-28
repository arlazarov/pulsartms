namespace Domain.Entities.Messaging;

// A provider's status for a message whose provider id no message under the
// business number knows yet: the provider reported it before the sender
// saved the id (audit F27). Kept briefly and applied when the id is saved.
public sealed class PendingDeliveryStatus : ICompanyOwned
{
  public Guid Id { get; set; }
  public Guid CompanyId { get; set; }
  public string Channel { get; set; } = "";
  public string BusinessNumberId { get; set; } = "";
  public string ProviderMessageId { get; set; } = "";
  public string Status { get; set; } = "";
  public DateTime At { get; set; }
  public int? ErrorCode { get; set; }
  public DateTime ReceivedAt { get; set; }
}
