namespace Domain.Entities.Messaging;

// One driver's thread with the carrier on one channel and one business
// number: a driver writing to two carrier numbers is two conversations.
// The driver is matched by the WhatsApp number on the driver's contacts,
// and stays unmatched when no driver, or more than one, has that number.
public sealed class Conversation : BaseEntity, ICompanyOwned
{
  public Guid CompanyId { get; set; }

  public string Channel { get; set; } = "";
  public string BusinessNumberId { get; set; } = "";
  public string Participant { get; set; } = "";
  public Guid? DriverId { get; set; }

  // When the driver last wrote: the provider's 24-hour window runs from it.
  public DateTime? LastInboundAt { get; set; }
  public DateTime LastMessageAt { get; set; }
  public string LastPreview { get; set; } = "";
  public Guid? LastMessageId { get; set; }

  // A dispatcher answering holds the conversation for a short while so a
  // colleague sees it; it is a courtesy, never a lock on sending.
  public Guid? ClaimedBy { get; set; }
  public DateTime? ClaimedUntil { get; set; }
  public long Revision { get; set; }
}
