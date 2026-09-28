using Domain.Rules.Routing;

namespace Server.Tests.Fuel;

// Audit F25: the terms of a fuel plan's cost have one owner. These pin
// the arithmetic of each term; which terms a caller adds, and from which
// inputs, is still the caller's and is pinned by that caller's tests,
// which record today's figures - not the product's final rules.
[Trait("Category", "Fuel")]
[Trait("Kind", "Unit")]
public sealed class FuelPlanCostTests
{
  [Fact]
  public void EachTermIsItsOwnArithmetic()
  {
    Assert.Equal(205, FuelPlanCost.Purchase(50, 4.1), 10);
    Assert.Equal(-5, FuelPlanCost.Purchase(50, -0.1), 10);
    Assert.Equal(21, FuelPlanCost.AccessTime(36, 35));
    Assert.Equal(132, FuelPlanCost.FutureFuel(100, 67, 4));
    Assert.Equal(0, FuelPlanCost.FutureFuel(100, 120, 4));
    Assert.Equal(205 + 40 + 21, FuelPlanCost.Stop(50, 4.1, 40, 36, 35), 10);
  }

  // A stop's cost is added in the order the optimizer and the replay
  // always added it, so moving them onto this owner changed no figure in
  // the last place.
  [Theory]
  [InlineData(37.3, 3.917, 25.5, 11.7, 42.25)]
  [InlineData(0.1, 0.2, 0.3, 0.7, 0.9)]
  [InlineData(123.456, 4.321, 17, 29.5, 38.4)]
  public void AStopAddsInTheSameOrderAsBefore(
    double gallons,
    double price,
    double stop,
    double minutes,
    double hourly
  ) =>
    Assert.Equal(
      gallons * price + stop + minutes / 60 * hourly,
      FuelPlanCost.Stop(gallons, price, stop, minutes, hourly)
    );
}
