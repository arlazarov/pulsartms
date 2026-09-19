using Application.Features.Routing.Models;

namespace Application.Features.Routing.Algorithms;

public static class FuelQuantityRedistribution
{
  public static FuelQuantityChoices? Prepare(
    int index,
    double miles,
    double startingGallons,
    TruckRouteProfile profile,
    IReadOnlyList<FuelCandidate> visits,
    IReadOnlyList<FuelPlanEditStop> edits,
    FuelArrivalPolicy arrival,
    int version,
    double initialAccessMiles,
    FuelManualReplayResult baseline,
    CancellationToken ct = default
  )
  {
    if (
      index < 0
      || index >= edits.Count
      || baseline.Plan.Stops.Count != edits.Count
      || baseline.PurchaseLimitsGallons[index] is not { } limit
      || !double.IsFinite(limit)
      || limit < 0
    )
      return null;
    var options = new List<FuelQuantityOption>();
    var maximum = Math.Max(25, Math.Ceiling(limit / 5) * 5);
    for (double value = 25; value <= maximum && value <= 500; value += 5)
    {
      ct.ThrowIfCancellationRequested();
      var full = value == maximum;
      var quantities = Redistribute(
        index,
        full ? limit : value,
        profile,
        baseline.Plan.Stops
      );
      var draft = edits
        .Select(
          (edit, i) =>
            edit with
            {
              BuyGallons = quantities[i],
              FillToTarget = i == index ? full : edit.FillToTarget,
              PurchaseLimitGallons = null,
            }
        )
        .ToList();
      var result = FuelManualReplay.Evaluate(
        miles,
        startingGallons,
        profile,
        visits,
        draft,
        arrival,
        version,
        initialAccessMiles
      );
      var plan = result.Plan;
      if (
        plan
          .Stops.Skip(index)
          .Any(stop =>
            !stop.FillToTarget
            && stop.BuyGallons
              < FuelOptimizer.MinimumAutomaticPurchaseGallons - 1e-9
          )
      )
        result.Errors.Add(
          "Each adjusted partial purchase needs at least 25 US gallons. Adjust or remove the stop."
        );
      options.Add(
        new(
          value,
          full,
          plan.Stops.Select(
              (stop, i) =>
                new FuelQuantityVisit(
                  stop.ArrivalGallons,
                  stop.BuyGallons,
                  stop.DepartureGallons,
                  stop.FillToTarget,
                  result.PurchaseLimitsGallons[i],
                  stop.PurchaseCostUsd
                )
            )
            .ToList(),
          plan.ArrivalGallons,
          plan.PurchaseGallons,
          plan.PurchaseCostUsd,
          plan.EconomicCostUsd,
          plan.ExpectedFutureFuelCostUsd,
          result.Errors
        )
      );
    }
    return new(index, options);
  }

  private static double[] Redistribute(
    int selected,
    double quantity,
    TruckRouteProfile profile,
    IReadOnlyList<FuelPlanStop> baseline
  )
  {
    var result = baseline.Select(stop => stop.BuyGallons).ToArray();
    var carry = quantity - result[selected];
    result[selected] = quantity;
    var capacity = profile.TankGallons!.Value * profile.FillPercent / 100;
    for (var i = selected + 1; i < result.Length; i++)
    {
      // Full tank is a target, not a fixed purchase or a partial-purchase
      // minimum.
      var minimum = FuelOptimizer.MinimumAutomaticPurchaseGallons;
      var headroom = Math.Max(
        0,
        capacity - (baseline[i].ArrivalGallons + carry)
      );
      var next = baseline[i].FillToTarget
        ? headroom
        : Math.Clamp(result[i] - carry, minimum, Math.Max(minimum, headroom));
      carry += next - result[i];
      result[i] = next;
    }
    return result;
  }
}
