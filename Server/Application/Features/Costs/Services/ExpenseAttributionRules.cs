using Domain.Entities.Costs;

namespace Application.Features.Costs.Services;

public sealed record AttributionShare(Guid DispatchId, decimal Amount);

public static class ExpenseAttributionRules
{
  public static bool ValidKind(string? value) =>
    value is "fuel" or "toll" or "lumper" or "maintenance" or "other";

  public static bool ValidBasis(string? value) =>
    value is "loaded-miles" or "equal-share" or "manual" or "contract";

  // What the expense still carries once the given attributions are counted.
  // Attributions are only ever summed within the expense's own currency,
  // which holds because an attribution has no currency of its own.
  public static decimal Unattributed(
    Expense expense,
    IEnumerable<ExpenseAttribution> attributions
  ) => expense.Amount - attributions.Sum(x => x.Amount);

  // An attribution must not create more cost than was spent, and a negative
  // share is not a correction but a different expense. The caller states the
  // complete intended set, so replacing a share is checked as a whole rather
  // than as a difference.
  public static string? Rejection(
    Expense expense,
    IReadOnlyCollection<AttributionShare> intended
  )
  {
    if (intended.Any(x => x.DispatchId == Guid.Empty))
      return "Each attribution needs the load that bears it.";
    if (intended.Select(x => x.DispatchId).Distinct().Count() != intended.Count)
      return "A load can bear only one share of an expense.";
    if (intended.Any(x => x.Amount < 0))
      return "An attributed amount cannot be negative.";
    return intended.Sum(x => x.Amount) > expense.Amount
      ? "Attributed amounts cannot exceed the expense."
      : null;
  }

  // Splits by a weight per load, giving the rounding remainder to the largest
  // weight so the parts always sum to the whole. Precision follows the
  // expense currency's own scale rather than one global rule.
  public static IReadOnlyList<AttributionShare> Split(
    decimal amount,
    IReadOnlyList<(Guid DispatchId, decimal Weight)> weights,
    int scale
  )
  {
    var usable = weights.Where(x => x.Weight > 0).ToArray();
    if (usable.Length == 0)
      return [];
    var total = usable.Sum(x => x.Weight);
    var shares = usable
      .Select(x => new AttributionShare(
        x.DispatchId,
        Math.Round(amount * x.Weight / total, scale, MidpointRounding.ToEven)
      ))
      .ToList();
    var remainder = amount - shares.Sum(x => x.Amount);
    if (remainder == 0)
      return shares;
    var largest = usable
      .Select((x, index) => (x.Weight, index))
      .OrderByDescending(x => x.Weight)
      .ThenBy(x => x.index)
      .First()
      .index;
    shares[largest] = shares[largest] with
    {
      Amount = shares[largest].Amount + remainder,
    };
    return shares;
  }
}
