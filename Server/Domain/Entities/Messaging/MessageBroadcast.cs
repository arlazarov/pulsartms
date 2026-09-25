namespace Domain.Entities.Messaging;

// One message a dispatcher sent to several drivers at once: each driver
// gets their own message in their own conversation, sent and followed like
// any other. The broadcast keeps what was asked, who it went to and why
// others were left out; its id is the dispatcher's retry key, so asking
// twice is one broadcast. Bounded: at most a few hundred recipients.
public sealed class MessageBroadcast : BaseEntity, ICompanyOwned
{
  public Guid CompanyId { get; set; }
  public Guid CreatedBy { get; set; }
  public DateTime CreatedAt { get; set; }

  // text or template.
  public string Kind { get; set; } = "";

  // The text each driver reads (a template's, filled).
  public string Body { get; set; } = "";

  // The template payload, for a template.
  public string? Template { get; set; }

  // Which drivers were asked for: all, a group (named) or those chosen.
  public string Scope { get; set; } = "";

  // Each driver asked for: their conversation and message, or why they
  // were left out.
  public string RecipientsJson { get; set; } = "[]";
  public DateTime? CancelledAt { get; set; }
}
