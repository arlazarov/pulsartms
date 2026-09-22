using Domain.Models.Routing;
using Domain.Rules.Routing;
using Server.Tests.Support;
using Xunit.Abstractions;

namespace Server.Tests.Fuel;

[Collection("Allocation measurements")]
[Trait("Category", "Fuel")]
[Trait("Kind", "Allocation")]
public sealed class FuelOptimizerAllocationTests(ITestOutputHelper output)
{
  [Fact]
  public void FractionalAlternativesDoNotAllocateSortingPipelines()
  {
    var profile = new TruckRouteProfile
    {
      Confirmed = true,
      TankGallons = 250,
      Mpg = 6.7,
      ReserveGallons = 25,
      FillPercent = 100,
      StopCostUsd = 20,
      DriverHourlyCostUsd = 35,
    };
    var candidates = Enumerable
      .Range(1, 12)
      .Select(i => new FuelCandidate(
        new FuelPlanStop
        {
          StationId = Guid.NewGuid(),
          Name = $"Station {i}",
          Point = new(40, -80),
          YourPrice = 3 + i % 3 * .1,
          EconomicPrice = 3 + i % 3 * .1,
          Currency = "USD",
          Unit = "US gal",
        },
        i * 110.3,
        .7,
        .9,
        3 + i % 3 * .1,
        3 + i % 3 * .1
      ))
      .ToArray();
    FuelPlan Calculate() =>
      FuelOptimizer.Optimize(1500, 70, profile, candidates, 1, false);
    var expected = Calculate();
    var before = GC.GetAllocatedBytesForCurrentThread();
    var actual = Calculate();
    var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    output.WriteLine($"Optimizer allocated {allocated:N0} bytes.");
    output.WriteLine(
      $"Cost {actual.EconomicCostUsd:R}; arrival {actual.ArrivalGallons:R}; "
        + string.Join(
          "; ",
          actual.Stops.Select(x => $"{x.Name}: {x.BuyGallons:R}")
        )
    );
    Assert.True(allocated < 20_000_000, $"Allocated {allocated:N0} bytes.");
    Assert.Equal(560, actual.EconomicCostUsd);
    Assert.Equal(25.8805970149254, actual.ArrivalGallons, 10);
    var purchase = Assert.Single(actual.Stops);
    Assert.Equal("Station 3", purchase.Name);
    Assert.Equal(180, purchase.BuyGallons);
    Assert.Equal(expected.EconomicCostUsd, actual.EconomicCostUsd);
    Assert.Equal(expected.ArrivalGallons, actual.ArrivalGallons);
    Assert.Equal(
      expected.Stops.Select(x => (x.StationId, x.BuyGallons)),
      actual.Stops.Select(x => (x.StationId, x.BuyGallons))
    );
  }
}
