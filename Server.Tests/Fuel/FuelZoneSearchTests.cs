using Domain.Models.Routing;
using Domain.Policies;
using Domain.Rules.Routing;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Unit")]
public class FuelZoneSearchTests
{
  private static FuelCandidate Station(
    string name,
    double mile,
    double price,
    double latitude,
    double longitude
  ) =>
    new(
      new()
      {
        StationId = Guid.NewGuid(),
        Name = name,
        Point = new(latitude, longitude),
      },
      mile,
      0,
      0,
      price,
      price
    );

  [Fact]
  public void RockHillReceivesARoadCheckEvenWhenProjectedOptimizerBuysNothing()
  {
    var rockHill = Station("LOVES #333", 89, 5.109, 35.0004855, -80.9782754);
    var statesville = Station("LOVES #497", 30, 5.213, 35.8127175, -80.8332419);
    var options = new FuelRegionOptions();
    var zones = FuelZoneSearch.Representatives(
      [statesville, rockHill],
      options
    );
    var scheduled = FuelZoneSearch.Schedule(
      [
        [],
      ],
      zones
    );
    Assert.Contains(
      scheduled.Take(options.CandidateRoadChecks),
      x => x.Contains(rockHill)
    );
    Assert.Empty(scheduled[0]);
    Assert.All(zones, x => Assert.Contains(x, new[] { statesville, rockHill }));
  }

  [Fact]
  public void WyomingFillIsConsideredWithBothNevadaTopUps()
  {
    var p = new TruckRouteProfile
    {
      Confirmed = true,
      TankGallons = 800 / 3.785411784,
      FillPercent = 90,
      ReserveGallons = 25,
      Mpg = 235.214583 / 35,
      StopCostUsd = 20,
    };
    var candidates = new[]
    {
      Station("310", 719, 5.105, 41.68, -107.98),
      Station("436", 971, 5.238, 40.72, -112.5),
      Station("365", 1148, 5.384, 40.72, -115.58),
      Station("246", 1454, 5.771, 39.61, -119.22),
    };
    var arrival = new FuelArrivalPolicy
    {
      MinimumGallons = 37,
      TargetGallons = 190,
      ReplacementPriceUsd = 6.778,
      PoorArea = true,
      TopUpPriceCeilingUsd = 6.428,
    };
    var chains = FuelRouteSearch.Chains(
      candidates,
      1706,
      p.TankGallons.Value,
      p,
      arrival
    );
    Assert.Contains(
      chains,
      x =>
        x.Select(s => s.Station.Name)
          .SequenceEqual(new[] { "310", "365", "246" })
    );
    var scheduled = FuelZoneSearch.Schedule(
      chains,
      FuelZoneSearch.Representatives(candidates, new())
    );
    Assert.Contains(
      scheduled.Take(12),
      x =>
        x.Select(s => s.Station.Name)
          .SequenceEqual(new[] { "310", "365", "246" })
    );
  }

  [Fact]
  public void ExpensiveBridgeAndCheaperCorridorFillSurviveManyDistantSeeds()
  {
    var bridge = Station("Expensive bridge", 50, 6, 40, -80);
    var cheap = Station("Cheaper corridor", 200, 3, 39, -80) with
    {
      ExtraInMiles = 1,
    };
    var fallback = Station("Expensive fill", 55, 6, 40, -80);
    var distant = Enumerable
      .Range(0, 20)
      .Select(i => new List<FuelCandidate>
      {
        Station($"Distant {i}", 100 + i, 2, 38, -80) with
        {
          ExtraInMiles = 20,
        },
      })
      .ToList();
    distant.Add([bridge, cheap]);
    distant.Add([fallback]);

    var scheduled = FuelZoneSearch.Schedule(
      distant,
      distant.Take(3).Select(x => x[0]).ToList()
    );
    var checkedChains = scheduled
      .Take(new FuelRegionOptions().CandidateRoadChecks)
      .ToList();

    Assert.Contains(
      checkedChains,
      chain => chain.SequenceEqual(new[] { bridge, cheap })
    );
    Assert.Contains(
      checkedChains,
      chain => chain.SequenceEqual(new[] { fallback })
    );
    Assert.Empty(checkedChains[0]);
    Assert.All(
      scheduled,
      chain =>
        Assert.Equal(chain.Count, chain.DistinctBy(x => x.VisitKey).Count())
    );
  }

  [Fact]
  public void NearRoadChainGetsAnEarlyCheckWithoutRepeatingTheSameVisit()
  {
    var physicalStation = Station("Corridor", 100, 5, 40, -80);
    var outbound = physicalStation with { LegIndex = 0, ExtraInMiles = .2 };
    var returning = physicalStation with
    {
      LegIndex = 2,
      AlongMiles = 900,
      ExtraInMiles = .2,
    };
    var cheap = Enumerable
      .Range(0, 12)
      .Select(i => new List<FuelCandidate>
      {
        Station($"Cheap {i}", 100 + i, 3, 40.5, -80) with
        {
          ExtraInMiles = 20,
        },
      })
      .ToList();
    cheap.Add([outbound, returning]);

    var scheduled = FuelZoneSearch.Schedule(cheap, [outbound, returning]);

    Assert.Empty(scheduled[0]);
    Assert.Equal(
      new[] { outbound.VisitKey, returning.VisitKey },
      scheduled[2].Select(x => x.VisitKey)
    );
    Assert.All(
      scheduled,
      chain =>
        Assert.Equal(chain.Count, chain.DistinctBy(x => x.VisitKey).Count())
    );
    Assert.Single(
      scheduled,
      chain =>
        chain
          .Select(x => x.VisitKey)
          .SequenceEqual(new[] { outbound.VisitKey, returning.VisitKey })
    );
  }
}
