namespace Domain.Entities.Costs;

// The part of an expense a load bears. An expense may have several, so one
// purchase can be divided between loads for different brokers. What is not
// attributed stays unattributed and is never implied by subtraction.
public sealed class ExpenseAttribution : BaseEntity, ICompanyOwned
{
  public Guid CompanyId { get; set; }

  public Guid ExpenseId { get; init; }
  public Guid DispatchId { get; init; }

  // In the expense's currency; the two are never summed across currencies.
  public decimal Amount { get; set; }

  // loaded-miles, equal-share, manual, contract.
  public string Basis { get; set; } = "manual";
  public string Reason { get; set; } = "";
  public bool ManualOverride { get; set; }

  public long Revision { get; set; }
  public DateTime RecordedAt { get; set; }
  public Guid RecordedBy { get; set; }
}
