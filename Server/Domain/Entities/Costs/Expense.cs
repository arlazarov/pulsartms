namespace Domain.Entities.Costs;

// Money that was spent. The amount exists before anyone decides which load
// bears it, so an expense makes no claim about a load; see ExpenseAttribution.
public sealed class Expense : BaseEntity
{
  // Only an imported expense has a natural key; one entered by hand has
  // none, and several such expenses must be able to exist. Null is distinct
  // in a unique index, so both cases hold.
  public Guid? IdempotencyKey { get; init; }

  // fuel, toll, lumper, maintenance, other.
  public string Kind { get; init; } = "other";
  public DateTime OccurredAt { get; init; }
  public string Location { get; init; } = "";

  public decimal Amount { get; init; }
  public string Currency { get; init; } = "";

  // Quantity and unit for an expense that has them, such as fuel volume.
  public decimal? Quantity { get; init; }
  public string QuantityUnit { get; init; } = "";

  public Guid? TruckId { get; init; }
  public Guid? DriverId { get; init; }
  public Guid? TrailerId { get; init; }
  public Guid? ExecutionLegId { get; init; }

  // What the source called the resources, kept exactly as supplied. Matching
  // these to internal resources is a separate step and must never rewrite
  // them, so a failed match leaves the source fact intact.
  public string SourceTruckName { get; init; } = "";
  public string SourceDriverName { get; init; } = "";
  public string Source { get; init; } = "";
  public string SourceReference { get; init; } = "";

  public long Revision { get; set; }
  public DateTime RecordedAt { get; init; }
  public Guid RecordedBy { get; init; }
}
