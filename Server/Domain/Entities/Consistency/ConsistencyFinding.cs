namespace Domain.Entities.Consistency;

// One occurrence of a rule failing for one entity. It is written when the
// detector first sees it, before any repair, and stays after resolution; a
// later failure of the same entity is a new occurrence, not a reopened one.
public sealed class ConsistencyFinding : BaseEntity, ICompanyOwned
{
  public Guid CompanyId { get; set; }

  public string Rule { get; set; } = "";
  public int RuleVersion { get; set; }
  public string EntityKey { get; set; } = "";
  public int Occurrence { get; set; }
  public string State { get; set; } = "open";
  public string Condition { get; set; } = "";
  public string Severity { get; set; } = "";
  public string Versions { get; set; } = "";
  public string EvidenceJson { get; set; } = "{}";
  public DateTime FirstSeenAt { get; set; }
  public DateTime LastSeenAt { get; set; }
  public DateTime LastTransitionAt { get; set; }
  public DateTime? ResolvedAt { get; set; }
  public int RepairAttempts { get; set; }
  public DateTime? LastRepairAt { get; set; }
  public string? LastRepairOutcome { get; set; }
  public bool Escalated { get; set; }
}
