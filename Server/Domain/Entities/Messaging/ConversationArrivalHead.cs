namespace Domain.Entities.Messaging;

// The company's arrival sequence for driver messages. Recording a driver
// message raises it in the same transaction; Sequence is a concurrency
// token, so a second writer that read the same value fails instead of
// committing an equal or smaller number later. Numbers therefore rise in
// commit order, which lets a notice tell a new message from an older one
// that merely came into view.
public sealed class ConversationArrivalHead : BaseEntity, ICompanyOwned
{
  public Guid CompanyId { get; set; }
  public long Sequence { get; set; }
}
