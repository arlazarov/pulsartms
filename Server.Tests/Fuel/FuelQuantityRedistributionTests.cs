using System.Text.Json;
using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Models;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Unit")]
public sealed class FuelQuantityRedistributionTests
{
  [Fact]
  public void TenMoreAtFirstReducesTheNextPurchaseByTenWithoutChangingArrival()
  {
    var (baseline, choices) = Prepare([25, 100, 25]);
    var option = choices.Options.Single(x => x.SliderGallons == 35);
    Assert.Empty(option.Errors);
    Assert.Equal(
      new[] { 35d, 90, 25 },
      option.Visits.Select(x => x.BuyGallons)
    );
    Assert.Equal(baseline.Plan.ArrivalGallons, option.ArrivalGallons, 10);
    Assert.Equal(baseline.Plan.PurchaseGallons, option.PurchaseGallons, 10);
    Assert.Equal(25, baseline.Plan.Stops[0].BuyGallons);
    Assert.Equal(100, baseline.Plan.Stops[1].BuyGallons);
    Assert.Equal(3, option.Visits.Count);
    Assert.True(JsonSerializer.SerializeToUtf8Bytes(choices).Length < 100_000);
  }

  [Fact]
  public void SurplusPassesTheMinimumPurchaseAndContinuesToTheThirdVisit()
  {
    var (baseline, choices) = Prepare([25, 30, 100]);
    var option = choices.Options.Single(x => x.SliderGallons == 35);
    Assert.Empty(option.Errors);
    Assert.Equal(
      new[] { 35d, 25, 95 },
      option.Visits.Select(x => x.BuyGallons)
    );
    Assert.Equal(baseline.Plan.ArrivalGallons, option.ArrivalGallons, 10);
  }

  [Fact]
  public void ReducingTheFirstPurchaseRestoresTheNextPurchase()
  {
    var (baseline, choices) = Prepare([35, 100, 25]);
    var option = choices.Options.Single(x => x.SliderGallons == 25);
    Assert.Empty(option.Errors);
    Assert.Equal(
      new[] { 25d, 110, 25 },
      option.Visits.Select(x => x.BuyGallons)
    );
    Assert.Equal(baseline.Plan.ArrivalGallons, option.ArrivalGallons, 10);
  }

  [Fact]
  public void EditingTheSecondPurchaseLeavesTheFirstUntouched()
  {
    var (baseline, choices) = Prepare([25, 100, 50], 1);
    var option = choices.Options.Single(x => x.SliderGallons == 110);
    Assert.Empty(option.Errors);
    Assert.Equal(
      new[] { 25d, 110, 40 },
      option.Visits.Select(x => x.BuyGallons)
    );
    Assert.Equal(baseline.Plan.ArrivalGallons, option.ArrivalGallons, 10);
  }

  [Theory]
  [InlineData(25, 35)]
  [InlineData(35, 25)]
  public void ADownstreamFullTargetAbsorbsEarlierChangesWithoutBecomingPartial(
    double initial,
    double changed
  )
  {
    var (baseline, choices) = Prepare([initial, 0], fullStops: [1]);
    var option = choices.Options.Single(x => x.SliderGallons == changed);

    Assert.Empty(option.Errors);
    Assert.True(option.Visits[1].FillToTarget);
    Assert.Equal(
      baseline.Plan.Stops[1].BuyGallons - (changed - initial),
      option.Visits[1].BuyGallons,
      10
    );
    Assert.Equal(250, option.Visits[1].DepartureGallons, 10);
    Assert.Equal(baseline.Plan.ArrivalGallons, option.ArrivalGallons, 10);
    Assert.True(baseline.Plan.Stops[1].FillToTarget);
  }

  [Fact]
  public void FullTargetsUseExactHeadroomBelowThePartialMinimumWithoutCarryingFalseSurplus()
  {
    var (baseline, choices) = Prepare([25, 0, 0], fullStops: [1, 2]);
    var option = choices.Options[^1];

    Assert.True(option.FillToTarget);
    Assert.Empty(option.Errors);
    Assert.Equal(
      new[] { 160d, 20, 20 },
      option.Visits.Select(x => x.BuyGallons)
    );
    Assert.All(
      option.Visits,
      visit =>
      {
        Assert.True(visit.FillToTarget);
        Assert.Equal(250, visit.DepartureGallons, 10);
      }
    );
    Assert.Equal(baseline.Plan.ArrivalGallons, option.ArrivalGallons, 10);
  }

  [Fact]
  public void FullTargetPreservesFractionalConfiguredCapacity()
  {
    var profile = new TruckRouteProfile
    {
      Confirmed = true,
      TankGallons = 211.3,
      Mpg = 5,
      ReserveGallons = 25,
      FillPercent = 90,
      StopCostUsd = 0,
      DriverHourlyCostUsd = 35,
    };
    var (baseline, choices) = Prepare(
      [25, 0],
      fullStops: [1],
      profile: profile
    );
    var option = choices.Options.Single(x => x.SliderGallons == 35);

    Assert.Empty(option.Errors);
    Assert.True(option.Visits[1].FillToTarget);
    Assert.Equal(85.17, option.Visits[1].BuyGallons, 10);
    Assert.Equal(190.17, option.Visits[1].DepartureGallons, 10);
    Assert.Equal(baseline.Plan.ArrivalGallons, option.ArrivalGallons, 10);
  }

  [Fact]
  public void ReachingTheNextFullTargetDoesNotChangeThePartialPurchaseAfterIt()
  {
    var (baseline, choices) = Prepare(
      [25, 0, 25],
      fullStops: [1],
      alongMiles: [50, 150, 350]
    );
    var option = choices.Options[^1];

    Assert.Empty(option.Errors);
    Assert.Equal(
      new[] { 160d, 20, 25 },
      option.Visits.Select(x => x.BuyGallons)
    );
    Assert.True(option.Visits[1].FillToTarget);
    Assert.False(option.Visits[2].FillToTarget);
    Assert.Equal(baseline.Plan.ArrivalGallons, option.ArrivalGallons, 10);
  }

  [Fact]
  public void EditingAfterAnEarlierFullTargetLeavesThatDecisionIntact()
  {
    var (_, choices) = Prepare([0, 0], selected: 1, fullStops: [0, 1]);
    var option = Assert.Single(choices.Options);

    Assert.Empty(option.Errors);
    Assert.Equal(160, option.Visits[0].BuyGallons, 10);
    Assert.All(option.Visits, visit => Assert.True(visit.FillToTarget));
  }

  [Fact]
  public void AFullPurchaseThatWouldOverfillALaterMinimumStopRemainsInvalid()
  {
    var (_, choices) = Prepare([25, 100, 25]);
    var full = choices.Options[^1];
    Assert.True(full.FillToTarget);
    Assert.Contains(full.Errors, error => error.Contains("fill limit"));
    Assert.All(full.Visits, visit => Assert.True(visit.BuyGallons >= 25));
  }

  [Fact]
  public void RemainingSurplusAtMinimumPurchasesIsRetainedAtTheFinish()
  {
    var (baseline, choices) = Prepare([25, 25, 25]);
    var option = choices.Options.Single(x => x.SliderGallons == 35);
    Assert.Empty(option.Errors);
    Assert.Equal(
      new[] { 35d, 25, 25 },
      option.Visits.Select(x => x.BuyGallons)
    );
    Assert.Equal(baseline.Plan.ArrivalGallons + 10, option.ArrivalGallons, 10);
  }

  [Fact]
  public void CancellationStopsPreparation()
  {
    using var cancel = new CancellationTokenSource();
    cancel.Cancel();
    Assert.Throws<OperationCanceledException>(
      () => Prepare([25, 100, 25], ct: cancel.Token)
    );
  }

  private static (
    FuelManualReplayResult Baseline,
    FuelQuantityChoices Choices
  ) Prepare(
    double[] quantities,
    int selected = 0,
    CancellationToken ct = default,
    int[]? fullStops = null,
    TruckRouteProfile? profile = null,
    double[]? alongMiles = null
  )
  {
    profile ??= new TruckRouteProfile
    {
      Confirmed = true,
      TankGallons = 250,
      Mpg = 5,
      ReserveGallons = 25,
      FillPercent = 100,
      StopCostUsd = 0,
      DriverHourlyCostUsd = 35,
    };
    var visits = quantities
      .Select(
        (_, i) =>
          new FuelCandidate(
            new FuelPlanStop
            {
              StationId = Guid.NewGuid(),
              BeforeStopId = Guid.NewGuid(),
              Point = new(40, -75 + i),
              Name = $"Station {i}",
              YourPrice = 4 + i,
              EconomicPrice = 3 + i,
              Currency = "USD",
              Unit = "US gal",
            },
            alongMiles?[i] ?? 50 + i * 100,
            0,
            0,
            4 + i,
            3 + i
          )
          {
            LegIndex = i,
          }
      )
      .ToList();
    var edits = visits
      .Select(
        (visit, i) =>
          new FuelPlanEditStop(
            visit.Station.StationId,
            visit.Station.BeforeStopId,
            quantities[i],
            fullStops?.Contains(i) == true
          )
      )
      .ToList();
    var arrival = new FuelArrivalPolicy
    {
      MinimumGallons = 25,
      TargetGallons = 250,
      ReplacementPriceUsd = 5,
    };
    var baseline = FuelManualReplay.Evaluate(
      600,
      100,
      profile,
      visits,
      edits,
      arrival,
      1
    );
    Assert.Empty(baseline.Errors);
    return (
      baseline,
      FuelQuantityRedistribution.Prepare(
        selected,
        600,
        100,
        profile,
        visits,
        edits,
        arrival,
        1,
        0,
        baseline,
        ct
      )!
    );
  }
}
