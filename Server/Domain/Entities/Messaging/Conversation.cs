namespace Domain.Entities.Messaging;

// One driver's thread with the carrier on one channel and one business
// number: a driver writing to two carrier numbers is two conversations.
// The driver is matched by the number their WhatsApp messages go to
// (DriverWhatsApp: their WhatsApp number, else their phone), and stays
// unmatched when no driver, or more than one, has that number. The number
// a conversation is with never changes: after a driver's number is edited,
// their history stays here and a new conversation serves the new number.
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

  // Raised with every committed change; a concurrency token, so it orders
  // the conversation's changes as they were committed.
  public long Revision { get; set; }

  // The conversation revision and the company arrival sequence at which
  // the latest driver message was recorded. Unread compares the revision,
  // notices the sequence, never the provider's time, so a message that
  // arrives late with an older time still counts.
  public long LastInboundRevision { get; set; }
  public long LastInboundSequence { get; set; }
}
