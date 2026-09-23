namespace Domain.Entities.Consistency;

// The last journal sequence handed out for a company. A journal write bumps
// it inside its own transaction, so the row lock orders writers: a later
// writer receives higher numbers only after the earlier one committed, and a
// reader paging by sequence never passes an event that commits afterwards.
public sealed class ConsistencyJournalHead : ICompanyOwned
{
  public Guid CompanyId { get; set; }
  public long LastSequence { get; set; }
}
