namespace Application.Features.Costs.Models;

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
