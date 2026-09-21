using Application.Features.Routing.Services.FuelPlanning;
using Domain.Models.Routing;
using Domain.Rules.Routing;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Unit")]
public sealed class FuelOptimizationMemoTests
{
  [Fact]
  public void IdenticalOrderedInputsReuseOneCalculationAndTakeTransfersOwnership()
  {
    var memo = Memo();
    var candidates = Candidates();
    var original = memo.Evaluate(candidates);
    var repeated = memo.Evaluate(
      candidates.Select(candidate => candidate with { }).ToArray()
    );
    Assert.Same(original.Plan, repeated.Plan);
    Assert.Same(original.Purchases, repeated.Purchases);
    Assert.Equal(1, memo.Calculations);
    Assert.Equal(1, memo.Reuses);
    var taken = memo.Take(candidates);
    Assert.Same(original.Plan, taken.Plan);
    Assert.Equal(2, memo.Reuses);
    var cost = taken.Plan.EconomicCostUsd;
    taken.Plan.EconomicCostUsd += 1000;
    taken.Purchases.Clear();

    var next = memo.Evaluate(candidates);
    Assert.NotSame(taken.Plan, next.Plan);
    Assert.NotSame(taken.Purchases, next.Purchases);
    Assert.NotEmpty(next.Purchases);
    Assert.Equal(cost, next.Plan.EconomicCostUsd);
    Assert.Equal(2, memo.Calculations);
    Assert.Equal(2, memo.Reuses);
    Assert.Equal(17, next.Plan.RouteVersion);
  }

  [Fact]
  public void CostAndFewestModesKeepSeparateResultsAndTakeOnlyRemovesTheCostResult()
  {
    var memo = Memo();
    var candidates = Candidates();
    var economic = memo.Evaluate(candidates);
    var fewest = memo.Evaluate(candidates, true);
    Assert.NotSame(economic.Plan, fewest.Plan);
    Assert.Equal(2, economic.Purchases.Count);
    Assert.Single(fewest.Purchases);
    Assert.True(economic.Plan.EconomicCostUsd < fewest.Plan.EconomicCostUsd);
    Assert.Same(economic.Plan, memo.Take(candidates).Plan);
    Assert.Same(fewest.Plan, memo.Evaluate(candidates, true).Plan);
    Assert.Equal(2, memo.Calculations);
    Assert.Equal(2, memo.Reuses);
  }

  [Theory]
  [InlineData("cash")]
  [InlineData("economic")]
  [InlineData("along")]
  [InlineData("access-in")]
  [InlineData("access-out")]
  [InlineData("entry")]
  [InlineData("exit")]
  [InlineData("detour-minutes")]
  [InlineData("leg")]
  public void ChangedCostOrGeometryInputsCannotReuseTheSameVisitResult(
    string field
  )
  {
    var memo = Memo();
    var candidates = Candidates();
    var original = memo.Evaluate(candidates);
    var candidate = candidates[0];
    var changed = field switch
    {
      "cash" => candidate with { PriceUsd = candidate.PriceUsd + .25 },
      "economic" => candidate with
      {
        EconomicPriceUsd = candidate.EconomicPriceUsd + .25,
      },
      "along" => candidate with { AlongMiles = candidate.AlongMiles + 1 },
      "access-in" => candidate with { ExtraInMiles = .2 },
      "access-out" => candidate with { ExtraOutMiles = .2 },
      "entry" => candidate with { EntryMiles = candidate.AlongMiles - 1 },
      "exit" => candidate with { ExitMiles = candidate.AlongMiles + 1 },
      "leg" => candidate with { LegIndex = 1 },
      "detour-minutes" => candidate with
      {
        Station = new FuelPlanStop
        {
          StationId = candidate.Station.StationId,
          Name = candidate.Station.Name,
          Point = candidate.Station.Point,
          YourPrice = candidate.Station.YourPrice,
          EconomicPrice = candidate.Station.EconomicPrice,
          Currency = candidate.Station.Currency,
          Unit = candidate.Station.Unit,
          DetourMinutes = 10,
        },
      },
      _ => throw new InvalidOperationException(),
    };
    var next = memo.Evaluate([changed, candidates[1]]);
    Assert.NotSame(original.Plan, next.Plan);
    Assert.Equal(2, memo.Calculations);
    Assert.Equal(0, memo.Reuses);
    Assert.Same(original.Plan, memo.Evaluate(candidates).Plan);
    Assert.Equal(1, memo.Reuses);
  }

  [Fact]
  public void ReorderingEqualMileVisitsDoesNotReuseTheOppositeTieOrder()
  {
    var memo = Memo();
    var candidates = Candidates();
    var tied = candidates[0] with
    {
      Station = new FuelPlanStop
      {
        StationId = Guid.NewGuid(),
        Name = "Equal bridge",
        Point = new(40, -80),
        YourPrice = 5,
        EconomicPrice = 5,
      },
    };
    var first = memo.Evaluate([candidates[0], tied, candidates[1]]);
    var reversed = memo.Evaluate([tied, candidates[0], candidates[1]]);
    Assert.NotSame(first.Plan, reversed.Plan);
    Assert.Equal(2, memo.Calculations);
    Assert.Equal(0, memo.Reuses);
  }

  private static FuelOptimizationMemo Memo() =>
    new(
      500,
      30,
      new()
      {
        Confirmed = true,
        TankGallons = 100,
        Mpg = 5,
        ReserveGallons = 10,
        FillPercent = 100,
        StopCostUsd = 0,
        DriverHourlyCostUsd = 35,
      },
      new()
      {
        MinimumGallons = 10,
        TargetGallons = 10,
        ReplacementPriceUsd = 5,
        EconomicPurchasesOnly = true,
      },
      17,
      0
    );

  private static FuelCandidate[] Candidates() =>
    [Station("Bridge", 70, 5), Station("Cheap", 200, 2)];

  private static FuelCandidate Station(
    string name,
    double mile,
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
        Currency = "USD",
        Unit = "US gal",
      },
      mile,
      0,
      0,
      price,
      price
    )
    {
      LegIndex = 0,
    };
}
