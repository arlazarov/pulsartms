using Domain.Models.Routing;
using Domain.Rules;
using Domain.Rules.Routing;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Unit")]
public sealed class FuelArrivalPolicyTests
{
  [Theory]
  [InlineData("reserve-only", true)]
  [InlineData("station", false)]
  [InlineData("name", false)]
  [InlineData("distance", false)]
  [InlineData("extra-target", false)]
  [InlineData("known-area", false)]
  [InlineData("forced-fill", false)]
  [InlineData("negative", false)]
  [InlineData("nonfinite", false)]
  public void UnpricedTerminalValueRequiresAnExplicitReserveOnlyPolicy(
    string scenario,
    bool valid
  )
  {
    var policy = UnknownArea();
    switch (scenario)
    {
      case "station":
        policy.EscapeStationId = Guid.NewGuid();
        break;
      case "name":
        policy.EscapeStationName = "Unpriced station";
        break;
      case "distance":
        policy.EscapeMiles = 10;
        break;
      case "extra-target":
        policy.TargetGallons++;
        break;
      case "known-area":
        policy.PoorArea = false;
        break;
      case "forced-fill":
        policy.EconomicPurchasesOnly = false;
        break;
      case "negative":
        policy.ReplacementPriceUsd = -1;
        break;
      case "nonfinite":
        policy.ReplacementPriceUsd = double.NaN;
        break;
    }
    Assert.Equal(valid, policy.HasValidReplacementValue());
    if (valid)
      Assert.Equal(
        180,
        FuelOptimizer
          .Optimize(100, 200, Profile(), [], 1, false, arrivalPolicy: policy)
          .ArrivalGallons
      );
    else
      Assert.Throws<RoutePlanningException>(
        () =>
          FuelOptimizer.Optimize(
            100,
            200,
            Profile(),
            [],
            1,
            false,
            arrivalPolicy: policy
          )
      );
    var manual = FuelManualReplay.Evaluate(
      100,
      200,
      Profile(),
      [],
      [],
      policy,
      1
    );
    Assert.Equal(valid, manual.Errors.Count == 0);
  }

  [Fact]
  public void KnownRoutePricesCanFundTheUnknownAreaReserveWithoutForcedFill()
  {
    var station = new FuelCandidate(
      new() { StationId = Guid.NewGuid(), Point = new(40, -100) },
      50,
      0,
      0,
      3,
      3
    );
    var policy = UnknownArea();
    var fuel = FuelOptimizer.Optimize(
      100,
      100,
      Profile(),
      [station],
      1,
      false,
      arrivalPolicy: policy
    );
    Assert.Equal(50, Assert.Single(fuel.Stops).BuyGallons);
    Assert.False(fuel.Stops[0].FillToTarget);
    Assert.Equal(130, fuel.ArrivalGallons);
    Assert.Equal(150, fuel.PurchaseCostUsd);
    Assert.Equal(0, fuel.ExpectedFutureFuelCostUsd);
  }

  [Fact]
  public void UnknownPriceCoverageDoesNotLowerTheArrivalFloor()
  {
    var policy = UnknownArea();
    Assert.Throws<RoutePlanningException>(
      () =>
        FuelOptimizer.Optimize(
          100,
          140,
          Profile(),
          [],
          1,
          false,
          arrivalPolicy: policy
        )
    );
    var manual = FuelManualReplay.Evaluate(
      100,
      140,
      Profile(),
      [],
      [],
      policy,
      1
    );
    Assert.Contains(manual.Errors, error => error.Contains("final arrival"));
    Assert.Equal(120, manual.Plan.ArrivalGallons);
  }

  private static FuelArrivalPolicy UnknownArea() =>
    new()
    {
      MinimumGallons = 125,
      TargetGallons = 125,
      PoorArea = true,
      EconomicPurchasesOnly = true,
    };

  private static TruckRouteProfile Profile() =>
    new()
    {
      Confirmed = true,
      TankGallons = 250,
      Mpg = 5,
      ReserveGallons = 25,
      FillPercent = 100,
    };
}
