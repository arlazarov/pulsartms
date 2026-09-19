using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Models;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Unit")]
public sealed class FuelScheduleRankingTests
{
  [Fact]
  public void AlreadyLatePlansCompareCostInsteadOfPayingAnyAmountToSaveOneMinute()
  {
    var expensive = Impact(
      Stop(1) with
      {
        BaselineLateMinutes = 346,
        CandidateLateMinutes = 347,
      }
    );
    var cheap = Impact(
      Stop(2) with
      {
        BaselineLateMinutes = 346,
        CandidateLateMinutes = 348,
      }
    );

    Assert.Equal(
      FuelScheduleRanking.For(expensive),
      FuelScheduleRanking.For(cheap)
    );
    var candidates = new[]
    {
      (Impact: expensive, Cost: 1850.1531666666667),
      (Impact: cheap, Cost: 1797.0622222222223),
    };
    Assert.Same(
      cheap,
      candidates
        .OrderBy(x => FuelScheduleRanking.For(x.Impact))
        .ThenBy(x => x.Cost)
        .First()
        .Impact
    );
  }

  [Fact]
  public void NewlyMissedAppointmentStillRanksBehindAnOnTimePlan()
  {
    var existingDelay = Stop(20) with
    {
      BaselineLateMinutes = 346,
      CandidateLateMinutes = 366,
    };
    var onTime = Impact(existingDelay, Stop(0));
    var newMiss = Impact(existingDelay, Stop(1));
    Assert.True(
      FuelScheduleRanking
        .For(newMiss)
        .CompareTo(FuelScheduleRanking.For(onTime)) > 0
    );
  }

  [Theory]
  [InlineData(6, 17, 18, 10.5)]
  [InlineData(6, 17, 617, 359.9166666666667)]
  [InlineData(2, 4, 4, 2.3333333333333335)]
  [InlineData(0, 60, 0, 35)]
  [InlineData(4.9, 60, 60, 35)]
  [InlineData(5, 60, 60, 35)]
  [InlineData(100, 60, 60, 35)]
  [InlineData(0, -10, 0, 0)]
  [InlineData(0, -10, 30, 17.5)]
  public void DelayCostIncludesExtraWaitingWithoutDoubleCountingRoadTime(
    double miles,
    double roadMinutes,
    int lateMinutes,
    double expected
  )
  {
    var impact = Impact(Stop(lateMinutes));
    Assert.Equal(
      expected,
      FuelScheduleRanking.DelayCost(impact, miles, roadMinutes, 35),
      6
    );
  }

  [Fact]
  public void ScheduleArrivalAndLatenessShareOneElapsedDelayCost()
  {
    var impact = Impact(Stop(90)) with { AddedMinutes = 80 };
    Assert.Equal(52.5, FuelScheduleRanking.DelayCost(impact, 1, 60, 35));
    Assert.Equal(0, FuelScheduleRanking.DelayCost(impact, 1, 60, 0));
  }

  [Fact]
  public void UnknownPickupAppointmentDoesNotHideKnownAddedDeliveryLateness()
  {
    var late = Impact(Stop(null), Stop(60));
    var unchanged = Impact(Stop(null), Stop(0));

    Assert.Null(late.AddedLateMinutes);
    Assert.Equal((0, 1, 60), FuelScheduleRanking.For(late));
    Assert.True(
      FuelScheduleRanking
        .For(late)
        .CompareTo(FuelScheduleRanking.For(unchanged)) > 0
    );
  }

  [Fact]
  public void EarlierBaselineShortageDoesNotHideNewShortageAtAnotherStop()
  {
    var prior = Stop(0) with { BaselineCycleShort = true, CycleShort = true };
    var introduced = Impact(prior, Stop(0) with { CycleShort = true });
    var existingOnly = Impact(prior, Stop(0));

    Assert.True(introduced.BaselineCycleShort);
    Assert.Equal(1, FuelScheduleRanking.For(introduced).Cycle);
    Assert.Equal(0, FuelScheduleRanking.For(existingOnly).Cycle);
    Assert.True(
      FuelScheduleRanking
        .For(introduced)
        .CompareTo(FuelScheduleRanking.For(existingOnly)) > 0
    );
  }

  [Fact]
  public void IncompleteAppointmentKnowledgeRemainsUnknownEvenWithKnownCycle()
  {
    Assert.Equal(
      1,
      FuelScheduleRanking.For(Impact(Stop(null), Stop(0))).Unknown
    );
    Assert.Equal(0, FuelScheduleRanking.For(Impact(Stop(0), Stop(0))).Unknown);
  }

  [Fact]
  public void UnverifiedCycleCannotProduceAKnownFeasibilityRank()
  {
    var unknown = Impact(
      Stop(0) with
      {
        CycleKnown = false,
        CycleShort = true,
      }
    ) with
    {
      CycleKnown = false,
    };
    Assert.Equal((0, 1, 0), FuelScheduleRanking.For(unknown));
  }

  [Theory]
  [InlineData(900, 50, 60, 35, 2, false)]
  [InlineData(970, 0, 60, 35, 2, true)]
  [InlineData(960, 5, 60, 35, 2, true)]
  [InlineData(960, 5, 60, 35, 1, false)]
  [InlineData(960, 40, -30, 35, 2, true)]
  [InlineData(1000, 0, 90, 0, 3, true)]
  [InlineData(900, 10, -60, 35, 2, false)]
  [InlineData(980.01, 0, 0, 35, 3, true)]
  [InlineData(980, 0, 0, 35, 3, false)]
  [InlineData(1019.99, 0, 0, 35, 1, false)]
  [InlineData(1020, 0, 0, 35, 1, true)]
  public void IdealIncumbentSkipsOnlyCandidatesWhoseCheckedCostCannotWin(
    double fuelCost,
    double futureCost,
    double extraMinutes,
    double hourlyCost,
    int purchases,
    bool expected
  )
  {
    var candidate = Plan(fuelCost, futureCost, purchases);
    Assert.Equal(
      expected,
      FuelScheduleRanking.CanSkipReplay(
        candidate,
        extraMinutes,
        hourlyCost,
        (0, 0, 0),
        1000,
        2
      )
    );
    Assert.Equal(fuelCost, candidate.EconomicCostUsd);
    Assert.Equal(futureCost, candidate.ExpectedFutureFuelCostUsd);
    Assert.Null(candidate.ScheduleImpact);
    Assert.Equal(purchases, candidate.Stops.Count);
  }

  [Theory]
  [InlineData(1, 0, 0)]
  [InlineData(0, 1, 0)]
  [InlineData(0, 0, 1)]
  [InlineData(1, 1, 60)]
  public void NonidealIncumbentAlwaysAllowsReplayToFindBetterFeasibility(
    int cycle,
    int unknown,
    int late
  )
  {
    Assert.False(
      FuelScheduleRanking.CanSkipReplay(
        Plan(2000, 0, 3),
        60,
        35,
        (cycle, unknown, late),
        1000,
        2
      )
    );
  }

  [Fact]
  public void MissingIncumbentCannotPruneTheFirstScheduleReplay()
  {
    Assert.False(
      FuelScheduleRanking.CanSkipReplay(Plan(2000, 0, 3), 60, 35, null, 1000, 2)
    );
  }

  [Theory]
  [InlineData("fuel-nan")]
  [InlineData("fuel-infinity")]
  [InlineData("fuel-negative")]
  [InlineData("future-nan")]
  [InlineData("future-infinity")]
  [InlineData("future-negative")]
  [InlineData("road-nan")]
  [InlineData("road-infinity")]
  [InlineData("road-negative-infinity")]
  [InlineData("hourly-nan")]
  [InlineData("hourly-infinity")]
  [InlineData("hourly-negative")]
  [InlineData("score-nan")]
  [InlineData("score-infinity")]
  [InlineData("score-negative")]
  [InlineData("purchase-count")]
  [InlineData("missing-purchases")]
  [InlineData("sum-overflow")]
  [InlineData("road-overflow")]
  public void InvalidOrUnboundedInputsNeverSuppressScheduleEvaluation(
    string invalid
  )
  {
    var candidate = Plan(2000, 10, 3);
    double road = 60,
      hourly = 35,
      score = 1000;
    var purchases = 2;
    switch (invalid)
    {
      case "fuel-nan":
        candidate.EconomicCostUsd = double.NaN;
        break;
      case "fuel-infinity":
        candidate.EconomicCostUsd = double.PositiveInfinity;
        break;
      case "fuel-negative":
        candidate.EconomicCostUsd = -1;
        break;
      case "future-nan":
        candidate.ExpectedFutureFuelCostUsd = double.NaN;
        break;
      case "future-infinity":
        candidate.ExpectedFutureFuelCostUsd = double.PositiveInfinity;
        break;
      case "future-negative":
        candidate.ExpectedFutureFuelCostUsd = -1;
        break;
      case "road-nan":
        road = double.NaN;
        break;
      case "road-infinity":
        road = double.PositiveInfinity;
        break;
      case "road-negative-infinity":
        road = double.NegativeInfinity;
        break;
      case "hourly-nan":
        hourly = double.NaN;
        break;
      case "hourly-infinity":
        hourly = double.PositiveInfinity;
        break;
      case "hourly-negative":
        hourly = -1;
        break;
      case "score-nan":
        score = double.NaN;
        break;
      case "score-infinity":
        score = double.PositiveInfinity;
        break;
      case "score-negative":
        score = -1;
        break;
      case "purchase-count":
        purchases = -1;
        break;
      case "missing-purchases":
        candidate.Stops = null!;
        break;
      case "sum-overflow":
        candidate.EconomicCostUsd = candidate.ExpectedFutureFuelCostUsd =
          double.MaxValue;
        break;
      case "road-overflow":
        road = double.MaxValue;
        hourly = 300;
        break;
    }
    Assert.False(
      FuelScheduleRanking.CanSkipReplay(
        candidate,
        road,
        hourly,
        (0, 0, 0),
        score,
        purchases
      )
    );
  }

  [Fact]
  public void PrunedCandidatesCannotBeatIncumbentEvenWithNoAdditionalScheduleDelay()
  {
    foreach (var futureCost in new[] { 0d, 20d })
    foreach (var roadMinutes in new[] { -10d, 0d, 20d, 60d })
    foreach (var delayMinutes in new[] { 0, 10, 120 })
    foreach (var purchases in new[] { 1, 2, 3 })
    {
      var candidate = Plan(990, futureCost, purchases);
      if (
        !FuelScheduleRanking.CanSkipReplay(
          candidate,
          roadMinutes,
          35,
          (0, 0, 0),
          1000,
          2
        )
      )
        continue;
      var replayCost =
        candidate.EconomicCostUsd
        + candidate.ExpectedFutureFuelCostUsd
        + FuelScheduleRanking.DelayCost(
          Impact(Stop(delayMinutes)),
          0,
          roadMinutes,
          35
        );
      Assert.True(FuelStopEconomy.Compare(replayCost, purchases, 1000, 2) >= 0);
    }
  }

  private static FuelPlan Plan(double cost, double futureCost, int purchases) =>
    new()
    {
      EconomicCostUsd = cost,
      ExpectedFutureFuelCostUsd = futureCost,
      Stops = Enumerable
        .Range(0, purchases)
        .Select(_ => new FuelPlanStop())
        .ToList(),
    };

  private static FuelStopScheduleImpact Stop(int? addedLate) =>
    new(
      Guid.NewGuid(),
      Guid.NewGuid(),
      DateTimeOffset.UnixEpoch,
      DateTimeOffset.UnixEpoch.AddMinutes(addedLate ?? 0),
      addedLate.HasValue ? 0 : null,
      addedLate,
      addedLate,
      60,
      60,
      true,
      false,
      false
    );

  private static FuelScheduleImpact Impact(
    params FuelStopScheduleImpact[] stops
  ) =>
    new(
      DateTime.UnixEpoch,
      true,
      true,
      stops.Any(stop => stop.BaselineCycleShort),
      stops.Any(stop => stop.CycleShort),
      0,
      stops.All(stop => stop.AddedLateMinutes.HasValue)
        ? stops.Max(stop => stop.AddedLateMinutes)
        : null,
      stops,
      null
    );
}
