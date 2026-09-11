using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Models;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Unit")]
public sealed class FuelOptionalTopUpTests
{
  [Fact]
  public void Captured11007SkipsDearerThirdStopWithDeliveryAndExitCovered()
  {
    var profile = Profile();
    profile.Mpg = 6.720416657142858;
    var policy = Policy();
    policy.MinimumGallons = 37;
    policy.ReplacementPriceUsd = 5.696;
    policy.EscapeMiles = 19.662395620980035;
    var plan = FuelOptimizer.Optimize(1436.653985243565, 135, profile,
      [Station("435", 564.8687696380051, 5.604, .5),
        Station("371", 863.9315342004342, 5.523, .5),
        Station("405", 1136.698607242638, 5.579, .5)], 1, false, arrivalPolicy: policy);

    Assert.Equal(new[] { "435", "371" }, plan.Stops.Select(x => x.Name));
    Assert.Equal(25, plan.Stops[0].BuyGallons);
    Assert.True(plan.Stops[1].FillToTarget);
    Assert.Equal(164.7043285725631, plan.ArrivalGallons, 8);
    Assert.True(plan.ArrivalGallons - policy.EscapeMiles / profile.Mpg >= profile.ReserveGallons);
    Assert.Equal(plan.PurchaseCostUsd + 8d / 60 * 35, plan.EconomicCostUsd, 8);
  }

  [Theory]
  [InlineData(false, true, 5, 80, 1)]
  [InlineData(true, true, 5, 80, 2)]
  [InlineData(false, false, 5, 80, 2)]
  [InlineData(false, true, 3, 80, 2)]
  [InlineData(false, true, 5, 130, 2)]
  public void OnlySkipsDearerOptionalStopsInKnownGoodAreas(bool poor, bool knownExit,
    double laterPrice, double minimum, int expectedStops)
  {
    var policy = Policy();
    policy.PoorArea = poor;
    policy.EscapeStationId = knownExit ? Guid.NewGuid() : Guid.Empty;
    policy.MinimumGallons = minimum;
    var plan = FuelOptimizer.Optimize(750, 40, Profile(),
      [Station("Main", 50, 4), Station("Later", 500, laterPrice)], 1, false, arrivalPolicy: policy);

    Assert.Equal(expectedStops, plan.Stops.Count);
    Assert.True(plan.ArrivalGallons >= minimum);
    Assert.All(plan.Stops, stop => Assert.InRange(stop.DepartureGallons, 25, 250));
    if (expectedStops == 1) Assert.True(plan.Stops[0].FillToTarget);
  }

  private static TruckRouteProfile Profile() => new() { Confirmed = true, TankGallons = 250,
    Mpg = 5, ReserveGallons = 25, FillPercent = 100, StopCostUsd = 0, DriverHourlyCostUsd = 35 };
  private static FuelArrivalPolicy Policy() => new() { MinimumGallons = 80, TargetGallons = 250,
    ReplacementPriceUsd = 6, EconomicPurchasesOnly = true, EscapeStationId = Guid.NewGuid(), EscapeMiles = 50 };
  private static FuelCandidate Station(string name, double miles, double price, double access = 0) => new(new()
    { StationId = Guid.NewGuid(), Name = name, Point = new(40, -80), YourPrice = price, EconomicPrice = price,
      Currency = "USD", Unit = "US gal", DetourMinutes = access > 0 ? 4 : 0 }, miles, access, access, price, price);
}
