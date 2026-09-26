using Domain.Models.Routing;
using Domain.Policies;
using Domain.Rules;
using Domain.Rules.Routing;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Unit")]
public class FuelRouteSearchTests
{
  private static TruckRouteProfile Profile() =>
    new()
    {
      Confirmed = true,
      TankGallons = 100,
      FillPercent = 100,
      ReserveGallons = 10,
      Mpg = 5,
      StopCostUsd = 20,
    };

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
        YourPrice = price,
      },
      mile,
      0,
      0,
      price,
      price
    );

  private static FuelArrivalPolicy PoorArea() =>
    new()
    {
      PoorArea = true,
      MinimumGallons = 20,
      TargetGallons = 100,
      ReplacementPriceUsd = 6,
      TopUpPriceCeilingUsd = 5.85,
    };

  [Fact]
  public void LongAssignedItineraryKeepsReachableStationsThroughTheFinalStop()
  {
    var candidates = Enumerable
      .Range(1, 79)
      .Select(i => Station($"Station {i}", i * 100, 4))
      .ToList();
    var arrival = new FuelArrivalPolicy
    {
      MinimumGallons = 10,
      TargetGallons = 10,
      ReplacementPriceUsd = 4,
    };
    var options = new FuelRegionOptions();

    var selected = FuelRouteSearch.SelectCandidates(
      candidates,
      8000,
      100,
      Profile(),
      arrival,
      options
    );
    var plan = FuelOptimizer.Optimize(
      8000,
      100,
      Profile(),
      selected,
      1,
      false,
      compare: false,
      arrivalPolicy: arrival
    );

    Assert.InRange(selected.Count, 19, options.CandidateShortlistLimit);
    Assert.True(selected.Max(x => x.AlongMiles) >= 7550);
    Assert.Equal(19, plan.Stops.Count);
    Assert.True(plan.ArrivalGallons >= 10);
    Assert.InRange(plan.Stops[0].ArrivalGallons, 0, 100);
    Assert.All(
      plan.Stops.Skip(1),
      stop => Assert.InRange(stop.ArrivalGallons, 10, 100)
    );
  }

  [Fact]
  public void FullRouteBackboneCanBeginWithABridgeAtTheCurrentPosition()
  {
    var start = Station("Starting bridge", 0, 5);
    var candidates = Enumerable
      .Range(1, 79)
      .Select(i => Station($"Station {i}", i * 100, 4))
      .Prepend(start)
      .ToList();
    var arrival = new FuelArrivalPolicy
    {
      MinimumGallons = 10,
      TargetGallons = 10,
      ReplacementPriceUsd = 4,
    };

    var selected = FuelRouteSearch.SelectCandidates(
      candidates,
      8000,
      10,
      Profile(),
      arrival,
      new()
    );
    var plan = FuelOptimizer.Optimize(
      8000,
      10,
      Profile(),
      selected,
      1,
      false,
      compare: false,
      arrivalPolicy: arrival
    );

    Assert.Contains(start, selected);
    Assert.Equal(start.Station.StationId, plan.Stops[0].StationId);
    Assert.True(selected.Max(x => x.AlongMiles) >= 7550);
    Assert.True(plan.ArrivalGallons >= 10);
  }

  [Fact]
  public void CompleteItineraryBeyondShortlistCapacityFailsExplicitly()
  {
    var candidates = Enumerable
      .Range(1, 79)
      .Select(i => Station($"Station {i}", i * 100, 4))
      .ToList();
    var arrival = new FuelArrivalPolicy
    {
      MinimumGallons = 10,
      TargetGallons = 10,
      ReplacementPriceUsd = 4,
    };

    var error = Assert.Throws<RoutePlanningException>(
      () =>
        FuelRouteSearch.SelectCandidates(
          candidates,
          8000,
          100,
          Profile(),
          arrival,
          new() { CandidateShortlistLimit = 8 }
        )
    );

    Assert.Contains("bounded search", error.Message);
    Assert.Contains("saved fuel plan has been kept", error.Message);
  }

  [Fact]
  public void CorridorBackboneSurvivesCheapDistantAlternativesOnLongItinerary()
  {
    var corridor = Enumerable
      .Range(1, 19)
      .Select(i => Station($"Corridor {i}", i * 400, 5))
      .ToList();
    var distant = Enumerable
      .Range(1, 79)
      .Select(i =>
        Station($"Distant {i}", i * 100, 3) with
        {
          ExtraInMiles = 20,
        }
      );
    var arrival = new FuelArrivalPolicy
    {
      MinimumGallons = 10,
      TargetGallons = 10,
      ReplacementPriceUsd = 5,
    };

    var selected = FuelRouteSearch.SelectCandidates(
      distant.Concat(corridor).ToList(),
      8000,
      100,
      Profile(),
      arrival,
      new()
    );

    Assert.All(corridor, station => Assert.Contains(station, selected));
    Assert.InRange(selected.Count, corridor.Count, 24);
  }

  [Theory]
  [InlineData(true, 1, 110)]
  [InlineData(false, 0, 240)]
  public void EconomicPolicyCanJustifySmallTopUpAboveEightyPercent(
    bool economic,
    int stops,
    double score
  )
  {
    var profile = Profile();
    profile.TankGallons = 250;
    var policy = new FuelArrivalPolicy
    {
      MinimumGallons = 50,
      TargetGallons = 250,
      ReplacementPriceUsd = 6,
      PoorArea = true,
      EconomicPurchasesOnly = economic,
    };

    var plan = FuelOptimizer.Optimize(
      200,
      250,
      profile,
      [Station("Cheap final top-up", 150, 1)],
      1,
      false,
      compare: false,
      arrivalPolicy: policy
    );

    Assert.Equal(stops, plan.Stops.Count);
    Assert.Equal(score, plan.EconomicCostUsd + plan.ExpectedFutureFuelCostUsd);
    if (economic)
    {
      Assert.Equal(30, Assert.Single(plan.Stops).BuyGallons);
      Assert.Equal(220, plan.Stops[0].ArrivalGallons);
      Assert.Equal(250, plan.Stops[0].DepartureGallons);
      Assert.Equal(240, plan.ArrivalGallons);
    }
  }

  [Fact]
  public void ReachableCheaperStationDoesNotRequireAnExpensiveBridgePurchase()
  {
    var expensive = Station("Expensive", 50, 6);
    var cheap = Station("Cheaper", 200, 3);
    var policy = new FuelArrivalPolicy
    {
      MinimumGallons = 20,
      TargetGallons = 20,
      ReplacementPriceUsd = 3,
    };
    var chains = FuelRouteSearch.Chains(
      [expensive, cheap],
      500,
      60,
      Profile(),
      policy
    );
    var plan = FuelOptimizer.Optimize(
      500,
      60,
      Profile(),
      chains[0],
      1,
      false,
      compare: false,
      arrivalPolicy: policy
    );

    Assert.Equal(cheap.Station.StationId, Assert.Single(plan.Stops).StationId);
    Assert.Equal(20, plan.Stops[0].ArrivalGallons);
    Assert.Equal(60, plan.Stops[0].BuyGallons);
    Assert.Equal(20, plan.ArrivalGallons);
  }

  [Fact]
  public void ExpensiveBridgeBuysOnlyEnoughToReachCheaperFuelWithReserve()
  {
    var bridge = Station("Expensive bridge", 50, 6);
    var cheap = Station("Cheaper fill", 200, 3);
    var policy = new FuelArrivalPolicy
    {
      MinimumGallons = 20,
      TargetGallons = 20,
      ReplacementPriceUsd = 3,
    };
    var chains = FuelRouteSearch.Chains(
      [bridge, cheap],
      500,
      30,
      Profile(),
      policy
    );
    var plan = FuelOptimizer.Optimize(
      500,
      30,
      Profile(),
      chains[0],
      1,
      false,
      compare: false,
      arrivalPolicy: policy
    );

    Assert.Equal(
      new[] { bridge.Station.StationId, cheap.Station.StationId },
      plan.Stops.Select(x => x.StationId)
    );
    Assert.Equal(25, plan.Stops[0].BuyGallons);
    Assert.False(plan.Stops[0].FillToTarget);
    Assert.Equal(15, plan.Stops[1].ArrivalGallons);
    Assert.Equal(70, plan.Stops[1].BuyGallons);
    Assert.Equal(25, plan.ArrivalGallons);
    Assert.Equal(360, plan.PurchaseCostUsd);
  }

  [Fact]
  public void LaterTopUpCanBeatTheCheapestSingleStopWithUsefulFuelRemaining()
  {
    var cheap = Station("Cheap early fill", 50, 3);
    var later = Station("Last affordable", 175, 4);
    var chains = FuelRouteSearch.Chains(
      [cheap, later],
      400,
      30,
      Profile(),
      PoorArea()
    );
    Assert.Equal(
      new[] { cheap.Station.StationId, later.Station.StationId },
      chains[0].Select(x => x.Station.StationId)
    );
    var plan = FuelOptimizer.Optimize(
      400,
      30,
      Profile(),
      chains[0],
      1,
      false,
      compare: false,
      arrivalPolicy: PoorArea()
    );
    Assert.All(plan.Stops, x => Assert.True(x.FillToTarget));
    Assert.Equal(25, plan.Stops[1].BuyGallons);
    Assert.Equal(55, plan.ArrivalGallons);
  }

  [Fact]
  public void DenseEarlyStationsDoNotExcludeTheLastAffordableTopUp()
  {
    var candidates = Enumerable
      .Range(0, 40)
      .Select(i => Station($"Early {i}", 10 + i, 3 + i * .001))
      .ToList();
    var later = Station("Last affordable", 350, 4);
    candidates.Add(later);
    var selected = FuelRouteSearch.SelectCandidates(
      candidates,
      400,
      30,
      Profile(),
      PoorArea(),
      new() { CandidateShortlistLimit = 8 }
    );
    Assert.Contains(later, selected);
    Assert.InRange(selected.Count, 1, 8);
  }

  [Fact]
  public void LongJourneyPreservesMultiplePurchasesWhenTestingCheaperReplacements()
  {
    var candidates = new[]
    {
      Station("First", 100, 3),
      Station("Middle", 400, 3.1),
      Station("Last", 750, 4),
      Station("Cheaper last", 780, 3.5),
    };
    var chains = FuelRouteSearch.Chains(
      candidates,
      1100,
      35,
      Profile(),
      PoorArea()
    );
    Assert.NotEmpty(chains);
    Assert.All(chains, chain => Assert.True(chain.Count >= 3));
    Assert.Contains(
      chains,
      chain => chain.Any(x => x.Station.Name == "Cheaper last")
    );
    foreach (var chain in chains)
    {
      var fuel = FuelOptimizer.Optimize(
        1100,
        35,
        Profile(),
        chain,
        1,
        false,
        compare: false,
        arrivalPolicy: PoorArea()
      );
      Assert.True(fuel.ArrivalGallons >= 20);
      Assert.All(
        fuel.Stops,
        stop => Assert.InRange(stop.ArrivalGallons, 10, 100)
      );
    }
  }

  [Fact]
  public void PenniesDoNotJustifyAnotherStop()
  {
    var profile = Profile();
    var policy = new FuelArrivalPolicy
    {
      MinimumGallons = 20,
      TargetGallons = 100,
      ReplacementPriceUsd = 5,
    };
    var chains = FuelRouteSearch.Chains(
      [Station("Almost same price", 50, 4.999)],
      100,
      100,
      profile,
      policy
    );
    Assert.Empty(chains[0]);
  }

  [Fact]
  public void CorridorCandidatesSurviveDenseCheapSectionsWithinTheSameShortlistLimit()
  {
    var nearby = Enumerable
      .Range(0, 9)
      .Select(i => Station($"Corridor {i}", i * 100 + 40, 5))
      .ToList();
    var distant = Enumerable
      .Range(0, 9)
      .SelectMany(i =>
        Enumerable
          .Range(0, 3)
          .Select(j =>
            Station($"Cheap {i}-{j}", i * 100 + 10 + j * 5, 3) with
            {
              ExtraInMiles = 20,
            }
          )
      )
      .ToList();

    var selected = FuelRouteSearch.SelectCandidates(
      distant.Concat(nearby).ToList(),
      900,
      40,
      Profile(),
      new(),
      new() { CandidateShortlistLimit = 12 }
    );

    Assert.All(nearby, station => Assert.Contains(station, selected));
    Assert.InRange(selected.Count, 1, 12);
    Assert.Equal(selected.Count, selected.DistinctBy(x => x.VisitKey).Count());
  }

  [Fact]
  public void LocalChainsKeepOriginalVisitAndProximityMetadataForRoadOrdering()
  {
    var station = Station("Access road", 50, 3) with
    {
      LegIndex = 2,
      ExtraInMiles = 7,
      ExtraOutMiles = 3,
    };

    var chains = FuelRouteSearch.Chains(
      [station],
      200,
      30,
      Profile(),
      new()
      {
        MinimumGallons = 20,
        TargetGallons = 20,
        ReplacementPriceUsd = 5,
      }
    );

    var purchase = Assert.Single(Assert.Single(chains));
    Assert.Same(station, purchase);
    Assert.Equal(7, purchase.ExtraInMiles);
    Assert.Equal(3, purchase.ExtraOutMiles);
    Assert.Equal(2, purchase.LegIndex);
  }

  [Fact]
  public void CorridorSearchKeepsFeasibleThreeStopChainsBeyondCheapEconomicSeeds()
  {
    var nearby = new[]
    {
      Station("Corridor first", 100, 5),
      Station("Corridor middle", 500, 5),
      Station("Corridor last", 900, 5),
    };
    var distant = new[]
    {
      Station("Cheap first", 120, 3),
      Station("Cheap middle", 510, 3),
      Station("Cheap last", 920, 3),
    }
      .Select(x => x with { ExtraInMiles = 15 })
      .ToList();
    var arrival = new FuelArrivalPolicy
    {
      MinimumGallons = 20,
      TargetGallons = 20,
      ReplacementPriceUsd = 5,
    };

    var chains = FuelRouteSearch.Chains(
      distant.Concat(nearby).ToList(),
      1200,
      40,
      Profile(),
      arrival
    );

    var corridor = Assert.Single(chains, chain => chain.SequenceEqual(nearby));
    var checkedFuel = FuelOptimizer.Optimize(
      1200,
      40,
      Profile(),
      corridor,
      0,
      false,
      compare: false,
      arrivalPolicy: arrival
    );
    Assert.True(checkedFuel.ArrivalGallons >= 20);
    Assert.All(checkedFuel.Stops, stop => Assert.True(stop.BuyGallons >= 10));
  }
}
