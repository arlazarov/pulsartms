namespace Domain.Entities.Messaging;

// When a number last wrote to the carrier's messaging number. WhatsApp
// accepts a free-form message only within 24 hours of that; nothing about
// what the driver wrote is kept.
public sealed class DriverMessagingWindow : BaseEntity, ICompanyOwned
{
  public Guid CompanyId { get; set; }
  public string Channel { get; set; } = "";
  public string Phone { get; set; } = "";
  public DateTime LastInboundAt { get; set; }
}
