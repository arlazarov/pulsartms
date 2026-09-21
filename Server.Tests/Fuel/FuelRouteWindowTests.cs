using Domain.Models.Routing;
using Domain.Rules.Routing;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Unit")]
public class FuelRouteWindowTests
{
  [Fact]
  public void AlternativeBypassUsesWideAnchorsWithoutSkippingDelivery()
  {
    var route = new TruckRoute { Legs = [new(200, 12000, [])] };
    var window = FuelRouteWindow.Create(route, 30, 170, 18);
    Assert.Equal(116, window.Entry);
    Assert.Equal(200, window.Exit);
    Assert.Equal(5040, window.BaselineSeconds);
  }

  [Fact]
  public void RequiredStopAndCurrentProgressBoundDiversion()
  {
    var route = new TruckRoute
    {
      Legs = [new(100, 6000, []), new(100, 12000, [])],
    };
    var window = FuelRouteWindow.Create(route, 120, 130, 30);
    Assert.Equal(120, window.Entry);
    Assert.Equal(200, window.Exit);
    Assert.Equal(9600, window.BaselineSeconds);
  }

  [Fact]
  public void AlternativeShortensInboundLegWithoutInventingExtraFuel()
  {
    var profile = new TruckRouteProfile
    {
      Confirmed = true,
      TankGallons = 100,
      Mpg = 5,
      ReserveGallons = 10,
      FillPercent = 100,
      StopCostUsd = 0,
    };
    var candidate = new FuelCandidate(
      new()
      {
        StationId = Guid.NewGuid(),
        Name = "Bypass",
        YourPrice = 3,
      },
      100,
      -10,
      15,
      3,
      3
    )
    {
      EntryMiles = 50,
      ExitMiles = 150,
    };
    var plan = FuelOptimizer.Optimize(300, 30, profile, [candidate], 1, false);
    var stop = Assert.Single(plan.Stops);
    Assert.Equal(90, stop.MilesAhead);
    Assert.Equal(12, stop.ArrivalGallons);
    Assert.Equal(5, stop.DetourMiles);
    Assert.Equal(50, stop.BuyGallons);
    Assert.Equal(30 + 50 - 305d / 5, plan.ArrivalGallons);
  }
}
