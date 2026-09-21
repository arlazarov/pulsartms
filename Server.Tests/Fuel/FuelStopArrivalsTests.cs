using Domain.Models.Routing;
using Domain.Rules.Routing;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Unit")]
public sealed class FuelStopArrivalsTests
{
  [Theory]
  [InlineData(false, 70, 50)]
  [InlineData(true, 68, 48)]
  public void PurchasesAffectOnlyTheirMandatoryStopAndLaterStops(
    bool estimated,
    double delivery,
    double nextPickup
  )
  {
    var load = Guid.NewGuid();
    var next = Guid.NewGuid();
    var visits = new[] { Visit(load, 100), Visit(load, 300), Visit(next, 400) };
    var fuel = new FuelPlan
    {
      StartingGallons = 100,
      EstimatedStationAccess = estimated,
      StartAccessMiles = 5,
      Stops =
      [
        new()
        {
          DispatchId = load,
          BeforeStopId = visits[1].Stop.Id,
          BuyGallons = 30,
          DetourMiles = 10,
        },
      ],
    };
    var result = FuelStopArrivals.Calculate(
      fuel,
      visits,
      new() { Mpg = 5, TankGallons = 250 }
    );
    Assert.Equal(
      new[] { 79d, delivery - 1, nextPickup - 1 },
      result.Select(x => x.Gallons)
    );
    Assert.Equal(31.6, result[0].Percent, 6);
    Assert.Equal(next, result[2].DispatchId);
    Assert.Equal(visits.Select(x => x.Stop.Id), result.Select(x => x.StopId));
  }

  [Fact]
  public void CurrentProgressAndLatestFuelDoNotCreditPassedPurchasesAgain()
  {
    var load = Guid.NewGuid();
    var stop = Visit(load, 300);
    var result = FuelStopArrivals.Calculate(
      new() { StartingGallons = 80 },
      [stop],
      new() { Mpg = 5, TankGallons = 250 },
      200
    );
    Assert.Equal(60, Assert.Single(result).Gallons);
    Assert.Equal(24, result[0].Percent);
  }

  [Theory]
  [InlineData("invalid-plan")]
  [InlineData("unknown-mpg")]
  [InlineData("unknown-capacity")]
  [InlineData("unowned-purchase")]
  [InlineData("unreachable")]
  [InlineData("invalid-distance")]
  public void UnknownOrInvalidInputsDoNotInventStopBalances(string condition)
  {
    var visit = Visit(
      Guid.NewGuid(),
      condition == "invalid-distance" ? double.NaN : 100
    );
    var fuel = new FuelPlan
    {
      StartingGallons = condition == "unreachable" ? 1 : 100,
      NeedsRefresh = condition == "invalid-plan",
    };
    if (condition == "unowned-purchase")
      fuel.Stops.Add(new() { BuyGallons = 10 });
    var profile = new TruckRouteProfile
    {
      Mpg = condition == "unknown-mpg" ? null : 5,
      TankGallons = condition == "unknown-capacity" ? null : 250,
    };
    Assert.Empty(FuelStopArrivals.Calculate(fuel, [visit], profile));
  }

  private static FuelItineraryStop Visit(Guid load, double miles) =>
    new(load, new(Guid.NewGuid(), "Stop", "", 1, new(40, -80)), miles);
}
