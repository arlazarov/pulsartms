namespace Domain.Entities.Consistency;

// The append-only audit journal: detection, repair and incident transitions.
// Readers page by Sequence, which ConsistencyJournalHead hands out in commit
// order per company; Id is only the row key.
public sealed class ConsistencyEvent : ICompanyOwned
{
  public Guid CompanyId { get; set; }

  public long Id { get; set; }
  public long Sequence { get; set; }
  public Guid FindingId { get; set; }
  public string Kind { get; set; } = "";
  public string Rule { get; set; } = "";
  public string EntityKey { get; set; } = "";
  public int Occurrence { get; set; }
  public Guid PassId { get; set; }
  public DateTime At { get; set; }
  public string DetailJson { get; set; } = "{}";
}
