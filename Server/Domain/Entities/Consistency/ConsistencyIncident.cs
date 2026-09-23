namespace Domain.Entities.Consistency;

// A technical incident: a finding that came back after a verified resolution
// or that repair could not settle. One row per company, rule and entity;
// each recurrence counts on the same row rather than opening another.
public sealed class ConsistencyIncident : BaseEntity, ICompanyOwned
{
  public Guid CompanyId { get; set; }

  public string Rule { get; set; } = "";
  public string EntityKey { get; set; } = "";
  public string Reason { get; set; } = "";
  public int Recurrences { get; set; }
  public Guid LastFindingId { get; set; }
  public DateTime OpenedAt { get; set; }
  public DateTime LastAt { get; set; }
}
