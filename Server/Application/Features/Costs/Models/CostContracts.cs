namespace Application.Features.Costs.Models;

public sealed record ExpenseEntry(
  string Kind,
  DateTime OccurredAt,
  decimal Amount,
  string Currency
)
{
  public Guid IdempotencyKey { get; init; }
  public string Location { get; init; } = "";
  public decimal? Quantity { get; init; }
  public string QuantityUnit { get; init; } = "";
  public Guid? TruckId { get; init; }
  public Guid? DriverId { get; init; }
  public Guid? TrailerId { get; init; }
  public Guid? ExecutionLegId { get; init; }
  public string SourceTruckName { get; init; } = "";
  public string SourceDriverName { get; init; } = "";
  public string Source { get; init; } = "";
  public string SourceReference { get; init; } = "";
}

public sealed record ExpenseShareUpdate(Guid DispatchId, decimal Amount);

public sealed record ExpenseAttributionUpdate(
  long Revision,
  string Basis,
  string Reason,
  IReadOnlyList<ExpenseShareUpdate> Shares
);

public sealed record ExpenseShareRow(
  Guid DispatchId,
  decimal Amount,
  string Basis,
  string Reason,
  bool ManualOverride
);

public sealed record ExpenseAttributionRow(
  Guid ExpenseId,
  long Revision,
  decimal Amount,
  string Currency,
  decimal Unattributed,
  IReadOnlyList<ExpenseShareRow> Shares
);

public sealed record LoadCostRow(
  Guid ExpenseId,
  string Kind,
  DateTime OccurredAt,
  string Location,
  decimal Amount,
  string Currency,
  decimal ExpenseAmount,
  string Basis,
  bool ManualOverride
);

public sealed record LoadCostTotal(
  string Currency,
  string Kind,
  decimal Amount
);

public sealed record LoadCostBreakdown(
  Guid DispatchId,
  IReadOnlyList<LoadCostTotal> Totals,
  IReadOnlyList<LoadCostRow> Rows,
  bool Truncated
);
