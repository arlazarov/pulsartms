namespace Domain.Rules;

// What an extra fuel stop has to be worth. A stop costs the driver time
// that no price shows, so a plan with one more stop must save at least
// this much against a feasible plan without it. It is a threshold for
// choosing between plans and never a charge: it is not added to a purchase,
// a cost or a reported saving.
public static class FuelStopEconomy
{
  public const double MinimumSavingsUsd = 20;

  public static IComparer<(double Cost, int Stops)> Comparer { get; } =
    Comparer<(double Cost, int Stops)>.Create(
      (left, right) => Compare(left.Cost, left.Stops, right.Cost, right.Stops)
    );

  public static int Compare(
    double cost,
    int stops,
    double otherCost,
    int otherStops
  )
  {
    // Ranking threshold only: never add it to purchases, costs, or reported
    // savings.
    var difference =
      cost - otherCost + MinimumSavingsUsd * (stops - otherStops);
    if (Math.Abs(difference) > 1e-8)
      return difference.CompareTo(0);
    // Exactly $20 per additional stop is sufficient.
    return otherStops.CompareTo(stops);
  }
}
