using System.Runtime.ExceptionServices;
using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Models;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Unit")]
public sealed class FuelRouteSearchMemoizationTests
{
  [Theory]
  [InlineData("bridge")]
  [InlineData("return")]
  [InlineData("equal-mile-ties")]
  [InlineData("terminal")]
  [InlineData("infeasible")]
  [InlineData("tier")]
  public void MemoizationPreservesUnmemoizedChainOrderAndEveryPurchaseResult(
    string scenario
  )
  {
    var profile = Profile();
    var arrival = Arrival();
    var candidates = new List<FuelCandidate>
    {
      Station("Bridge", 70, 5),
      Station("Main fill", 200, 2, 1),
      Station("Alternative", 220, 2.1, 6),
      Station("Late fill", 400, 3, 2),
    };
    var miles = 500d;
    var gallons = 30d;
    if (scenario == "return")
    {
      candidates.Add(candidates[1] with { AlongMiles = 800, LegIndex = 2 });
      candidates.Add(Station("Turn", 500, 4, 0, 1));
      miles = 900;
      gallons = 60;
    }
    if (scenario == "equal-mile-ties")
      candidates.Insert(1, Station("Equal bridge", 70, 5));
    if (scenario == "terminal")
    {
      arrival.MinimumGallons = 50;
      arrival.TargetGallons = 100;
      arrival.PoorArea = true;
    }
    if (scenario == "infeasible")
      miles = 3000;
    if (scenario == "tier")
    {
      candidates =
      [
        Station("Cheap first", 100, 3),
        Station("Cheap middle", 400, 3),
        Station("Cheap final", 800, 3),
        Station("Dearer first", 250, 3.01),
        Station("Dearer final", 650, 3.01),
      ];
      miles = 1100;
      gallons = 60;
    }
    var original = candidates
      .Select(candidate =>
        (candidate.VisitKey, candidate.ExtraInMiles, candidate.ExtraOutMiles)
      )
      .ToArray();
    var expected = UnmemoizedFuelSearch.Chains(
      candidates,
      miles,
      gallons,
      profile,
      arrival
    );
    var actual = FuelRouteSearch.Chains(
      candidates,
      miles,
      gallons,
      profile,
      arrival
    );

    Assert.Equal(expected.Select(Key), actual.Select(Key));
    Assert.Equal(
      original,
      candidates.Select(candidate =>
        (candidate.VisitKey, candidate.ExtraInMiles, candidate.ExtraOutMiles)
      )
    );
    for (var i = 0; i < actual.Count; i++)
    {
      var before = Optimize(expected[i]);
      var after = Optimize(actual[i]);
      Assert.Equal(before.EconomicCostUsd, after.EconomicCostUsd);
      Assert.Equal(
        before.ExpectedFutureFuelCostUsd,
        after.ExpectedFutureFuelCostUsd
      );
      Assert.Equal(before.ArrivalGallons, after.ArrivalGallons);
      Assert.Equal(
        before.Stops.Select(stop => stop.BuyGallons),
        after.Stops.Select(stop => stop.BuyGallons)
      );
      Assert.All(
        actual[i],
        candidate =>
          Assert.Contains(
            candidates,
            originalCandidate => ReferenceEquals(candidate, originalCandidate)
          )
      );
    }
    FuelPlan Optimize(List<FuelCandidate> chain) =>
      FuelOptimizer.Optimize(
        miles,
        gallons,
        profile,
        chain
          .Select(candidate =>
            candidate with
            {
              ExtraInMiles = 0,
              ExtraOutMiles = 0,
            }
          )
          .ToList(),
        0,
        false,
        compare: false,
        arrivalPolicy: arrival
      );
  }

  [Fact]
  public void IdenticalInfeasibleSubsetsAreSolvedOncePerModeWithoutAProductionCounter()
  {
    var candidates = new[]
    {
      Station("A", 100, 3),
      Station("B", 200, 3),
      Station("C", 300, 3),
    };
    var originalFailures = CountFailures(
      () =>
        UnmemoizedFuelSearch.Chains(candidates, 3000, 30, Profile(), Arrival())
    );
    var memoizedFailures = CountFailures(
      () => FuelRouteSearch.Chains(candidates, 3000, 30, Profile(), Arrival())
    );

    Assert.Equal(13, originalFailures);
    Assert.Equal(9, memoizedFailures);
    Assert.Equal(
      9,
      CountFailures(
        () => FuelRouteSearch.Chains(candidates, 3000, 30, Profile(), Arrival())
      )
    );
  }

  [Fact]
  public void CostAndFewestStopModesBothKeepTheirDistinctChains()
  {
    var bridge = Station("Bridge", 70, 5);
    var cheap = Station("Cheap", 200, 2);
    var chains = FuelRouteSearch.Chains(
      [bridge, cheap],
      500,
      30,
      Profile(),
      Arrival()
    );
    Assert.Equal(
      new[] { bridge.VisitKey, cheap.VisitKey },
      chains[0].Select(candidate => candidate.VisitKey)
    );
    Assert.Contains(
      chains,
      chain => chain.Count == 1 && chain[0].VisitKey == bridge.VisitKey
    );
  }

  [Fact]
  public void RequestLocalMemoCannotReuseChangedInputsAndCancellationIsStillObserved()
  {
    var candidates = new[]
    {
      Station("Bridge", 70, 5),
      Station("Cheap", 200, 2),
    };
    Assert.NotEmpty(
      FuelRouteSearch.Chains(candidates, 500, 30, Profile(), Arrival())
    );
    Assert.Empty(
      FuelRouteSearch.Chains(candidates, 3000, 30, Profile(), Arrival())
    );
    using var cancelled = new CancellationTokenSource();
    cancelled.Cancel();
    Assert.Throws<OperationCanceledException>(
      () =>
        FuelRouteSearch.Chains(
          candidates,
          500,
          30,
          Profile(),
          Arrival(),
          cancelled.Token
        )
    );
  }

  private static int CountFailures(Func<List<List<FuelCandidate>>> calculate)
  {
    var thread = Environment.CurrentManagedThreadId;
    var count = 0;
    void Failed(object? sender, FirstChanceExceptionEventArgs args)
    {
      if (
        Environment.CurrentManagedThreadId == thread
        && args.Exception is RoutePlanningException
      )
        count++;
    }
    AppDomain.CurrentDomain.FirstChanceException += Failed;
    try
    {
      Assert.Empty(calculate());
    }
    finally
    {
      AppDomain.CurrentDomain.FirstChanceException -= Failed;
    }
    return count;
  }

  private static string Key(List<FuelCandidate> chain) =>
    string.Join(",", chain.Select(candidate => candidate.VisitKey));

  private static TruckRouteProfile Profile() =>
    new()
    {
      Confirmed = true,
      TankGallons = 100,
      Mpg = 5,
      ReserveGallons = 10,
      FillPercent = 100,
      StopCostUsd = 20,
      DriverHourlyCostUsd = 35,
    };

  private static FuelArrivalPolicy Arrival() =>
    new()
    {
      MinimumGallons = 10,
      TargetGallons = 10,
      ReplacementPriceUsd = 5,
      EconomicPurchasesOnly = true,
    };

  private static FuelCandidate Station(
    string name,
    double mile,
    double price,
    double access = 0,
    int leg = 0
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
      access,
      0,
      price,
      price
    )
    {
      LegIndex = leg,
    };
}
