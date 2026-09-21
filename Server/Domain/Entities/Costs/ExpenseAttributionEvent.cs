namespace Domain.Entities.Costs;

// Immutable history of one attribution decision, including the decision to
// remove one. An amount of zero with no load records that a share was undone.
public sealed class ExpenseAttributionEvent : BaseEntity, ICompanyOwned
{
  public Guid CompanyId { get; set; }

  public Guid ExpenseId { get; init; }
  public Guid? AttributionId { get; init; }
  public Guid? DispatchId { get; init; }
  public long Revision { get; init; }
  public decimal PreviousAmount { get; init; }
  public decimal Amount { get; init; }
  public string Basis { get; init; } = "manual";
  public string Reason { get; init; } = "";
  public bool ManualOverride { get; init; }
  public DateTime RecordedAt { get; init; }
  public Guid RecordedBy { get; init; }
}
