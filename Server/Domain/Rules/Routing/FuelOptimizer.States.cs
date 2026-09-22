using Domain.Models.Routing;

namespace Domain.Rules.Routing;

public static partial class FuelOptimizer
{
  private static bool Dominates(
    List<State> alternatives,
    double fuel,
    double cost,
    int count,
    bool fewestStops
  )
  {
    foreach (var state in alternatives)
      if (
        state.Fuel == fuel
        && state.Cost <= cost
        && (!fewestStops || state.Purchases.Count <= count)
      )
        return true;
    return false;
  }

  private static void RetainAlternatives(
    List<State> alternatives,
    bool fewestStops,
    FuelArrivalPolicy? arrival
  )
  {
    var cash = alternatives[0];
    var terminal = cash;
    var range = cash;
    var unrestrictedRange = cash;
    for (var i = 1; i < alternatives.Count; i++)
    {
      var candidate = alternatives[i];
      if (Compare(candidate, cash, 0) < 0)
        cash = candidate;
      if (Compare(candidate, terminal, 1) < 0)
        terminal = candidate;
      if (Compare(candidate, range, 2) < 0)
        range = candidate;
      if (Compare(candidate, unrestrictedRange, 3) < 0)
        unrestrictedRange = candidate;
    }
    // Keep the original stable ranking and distinct-winner order.
    alternatives.Clear();
    alternatives.Add(cash);
    Add(terminal);
    Add(range);
    Add(unrestrictedRange);

    void Add(State state)
    {
      if (!alternatives.Contains(state))
        alternatives.Add(state);
    }

    int Compare(State left, State right, int ranking)
    {
      if (fewestStops && ranking != 3)
      {
        var stops = left.Purchases.Count.CompareTo(right.Purchases.Count);
        if (stops != 0)
          return stops;
      }
      if (ranking >= 2)
      {
        var fuel = right.Fuel.CompareTo(left.Fuel);
        if (fuel != 0)
          return fuel;
      }
      if (ranking == 1 && arrival is not null)
        return Score(left).CompareTo(Score(right));
      return left.Cost.CompareTo(right.Cost);
    }

    double Score(State state) =>
      state.Cost
      + Math.Max(0, arrival!.TargetGallons - state.Fuel)
        * arrival.ReplacementPriceUsd;
  }
}
