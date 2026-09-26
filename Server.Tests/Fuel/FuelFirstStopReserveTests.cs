using Domain.Models.Routing;
using Domain.Rules.Routing;

namespace Server.Tests.Fuel;

// Truck 11007 on September 26: 97.5 US gal, 6.72 mpg, a 250 gal tank and a
// 25 gal reserve, 1,230 miles to the delivery. The plan sent it 616 miles
// to the cheapest station, arriving with 5.6 gal, although a station on
// the road at mile 279 kept the reserve for a few dollars more. Reaching
// the first purchase below the reserve stays allowed, but only when no
// plan keeps it.
[Trait("Category", "Fuel")]
[Trait("Kind", "Unit")]
public sealed class FuelFirstStopReserveTests
{
  private const double Miles = 1230;
  private const double Start = 97.5;

  [Fact]
  public void APlanThatKeepsTheReserveToItsFirstStopWinsOverACheaperOne()
  {
    var profile = Profile();
    var plan = FuelOptimizer.Optimize(
      Miles,
      Start,
      profile,
      [
        Station("LOVES #820", 278.8, 5.814),
        Station("LOVES #504", 615.6, 5.624),
        Station("LOVES #432", 1300, 5.639),
      ],
      1,
      false,
      compare: false,
      arrivalPolicy: Arrival(profile)
    );

    var first = plan.Stops[0];
    Assert.Equal("LOVES #820", first.Name);
    Assert.True(first.ArrivalGallons >= profile.ReserveGallons);
    Assert.Equal("", first.Warning ?? "");
    // Every later stop keeps the reserve too, as before.
    Assert.All(
      plan.Stops,
      stop => Assert.True(stop.ArrivalGallons >= profile.ReserveGallons)
    );
  }

  [Fact]
  public void WithNoStationThatKeepsTheReserveTheFirstMayStillBeReachedBelowIt()
  {
    var profile = Profile();
    var plan = FuelOptimizer.Optimize(
      Miles,
      Start,
      profile,
      [Station("LOVES #504", 615.6, 5.624), Station("LOVES #432", 1300, 5.639)],
      1,
      false,
      compare: false,
      arrivalPolicy: Arrival(profile)
    );

    var first = plan.Stops[0];
    Assert.Equal("LOVES #504", first.Name);
    Assert.InRange(first.ArrivalGallons, 0, profile.ReserveGallons);
    Assert.StartsWith("Below reserve", first.Warning);
  }

  private static TruckRouteProfile Profile() =>
    new()
    {
      Confirmed = true,
      TankGallons = 250,
      Mpg = 6.720416657142858,
      ReserveGallons = 25,
      FillPercent = 100,
      StopCostUsd = 0,
      DriverHourlyCostUsd = 35,
    };

  private static FuelArrivalPolicy Arrival(TruckRouteProfile profile) =>
    new()
    {
      MinimumGallons = 125,
      TargetGallons = 250,
      ReplacementPriceUsd = 5.864,
      EconomicPurchasesOnly = true,
    };

  private static FuelCandidate Station(
    string name,
    double miles,
    double price
  ) =>
    new(
      new()
      {
        StationId = Guid.NewGuid(),
        Name = name,
        Point = new(40, -80),
        YourPrice = price,
        EconomicPrice = price,
        Unit = "US gal",
        Currency = "USD",
      },
      miles,
      0,
      0,
      price,
      price
    )
    {
      LegIndex = 0,
    };
}
