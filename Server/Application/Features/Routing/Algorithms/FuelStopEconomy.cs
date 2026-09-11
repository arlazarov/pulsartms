namespace Application.Features.Routing.Algorithms;

public static class FuelStopEconomy
{
  public const double MinimumSavingsUsd = 20;

  public static IComparer<(double Cost, int Stops)> Comparer { get; } =
    System.Collections.Generic.Comparer<(double Cost, int Stops)>.Create((left, right) =>
      Compare(left.Cost, left.Stops, right.Cost, right.Stops));

  public static int Compare(double cost, int stops, double otherCost, int otherStops)
  {
    // Ranking threshold only: never add it to purchases, costs, or reported savings.
    var difference = cost - otherCost + MinimumSavingsUsd * (stops - otherStops);
    if (Math.Abs(difference) > 1e-8) return difference.CompareTo(0);
    // Exactly $20 per additional stop is sufficient.
    return otherStops.CompareTo(stops);
  }
}
