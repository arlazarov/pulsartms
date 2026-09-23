namespace Domain.Entities.Messaging;

// How far one dispatcher has read a conversation. This is PulsR's own
// unread count; it tells the driver nothing.
public sealed class ConversationRead : BaseEntity, ICompanyOwned
{
  public Guid CompanyId { get; set; }

  public Guid ConversationId { get; set; }
  public Guid UserId { get; set; }
  public DateTime ReadThrough { get; set; }
}
