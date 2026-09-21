using Domain.Models.Routing;
using Domain.Rules.Routing;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Unit")]
public sealed class FuelArrivalForecastTests
{
  [Fact]
  public void CurrentFuelForecastRemainsAvailableWithoutAFuelPlan()
  {
    var dispatch = Guid.NewGuid();
    var pickup = Guid.NewGuid();
    var delivery = Guid.NewGuid();
    var state = State(dispatch, pickup, delivery);

    var result = FuelArrivalForecast.Calculate(state);

    Assert.Collection(
      result.OrderBy(value => value.StopId == pickup ? 0 : 1),
      value =>
      {
        Assert.Equal(pickup, value.StopId);
        Assert.Equal(90, value.Gallons, 6);
      },
      value =>
      {
        Assert.Equal(delivery, value.StopId);
        Assert.Equal(70, value.Gallons, 6);
      }
    );
  }

  [Fact]
  public void ValidPlannedArrivalWinsOverTheBaselineEstimate()
  {
    var dispatch = Guid.NewGuid();
    var pickup = Guid.NewGuid();
    var delivery = Guid.NewGuid();
    var state = State(dispatch, pickup, delivery);
    state.Plan!.FuelPlan = new()
    {
      StopArrivals = [new(dispatch, delivery, 90, 45)],
    };

    var result = FuelArrivalForecast.Calculate(state);

    Assert.Equal(90, result.Single(value => value.StopId == delivery).Gallons);
    Assert.Equal(90, result.Single(value => value.StopId == pickup).Gallons);
  }

  [Fact]
  public void InvalidRecommendationDoesNotHideTheCurrentFuelOnlyEstimate()
  {
    var dispatch = Guid.NewGuid();
    var pickup = Guid.NewGuid();
    var delivery = Guid.NewGuid();
    var state = State(dispatch, pickup, delivery);
    state.Plan!.FuelPlan = new()
    {
      NeedsRefresh = true,
      StopArrivals = [new(dispatch, delivery, 199, 99.5)],
    };

    var result = FuelArrivalForecast.Calculate(state);

    Assert.Equal(70, result.Single(value => value.StopId == delivery).Gallons);
    Assert.Equal(90, result.Single(value => value.StopId == pickup).Gallons);
  }

  [Theory]
  [InlineData("changed-route")]
  [InlineData("completed-route")]
  [InlineData("missing-progress")]
  [InlineData("stale-gps")]
  [InlineData("missing-position")]
  [InlineData("invalid-position")]
  [InlineData("missing-gps-time")]
  [InlineData("missing-progress-miles")]
  [InlineData("invalid-progress-miles")]
  [InlineData("negative-progress-miles")]
  public void UnconfirmedRouteProgressCannotPublishArrivalFuel(string invalid)
  {
    var dispatch = Guid.NewGuid();
    var pickup = Guid.NewGuid();
    var delivery = Guid.NewGuid();
    var state = State(dispatch, pickup, delivery);
    state.Plan!.FuelPlan = new()
    {
      StopArrivals = [new(dispatch, delivery, 90, 45)],
    };
    if (invalid == "changed-route")
      state.Plan.InputsChanged = true;
    if (invalid == "completed-route")
      state.Plan.Tracking.AllStopsPassed = true;
    state = state with
    {
      Progress = invalid switch
      {
        "missing-progress" => null,
        "stale-gps" => state.Progress! with { LocationStale = true },
        "missing-position" => state.Progress! with { Position = null },
        "invalid-position" => state.Progress! with
        {
          Position = new(double.NaN, 1),
        },
        "missing-gps-time" => state.Progress! with { LocationTime = null },
        "missing-progress-miles" => state.Progress! with
        {
          ProgressMiles = null,
        },
        "invalid-progress-miles" => state.Progress! with
        {
          ProgressMiles = double.NaN,
        },
        "negative-progress-miles" => state.Progress! with
        {
          ProgressMiles = -1,
        },
        _ => state.Progress,
      },
    };

    Assert.Empty(FuelArrivalForecast.Calculate(state));
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public void OffRoadPositionPreservesValidatedAccessButCannotInventBaseline(
    bool projected
  )
  {
    var dispatch = Guid.NewGuid();
    var pickup = Guid.NewGuid();
    var delivery = Guid.NewGuid();
    var state = State(dispatch, pickup, delivery);
    state = state with { Progress = state.Progress! with { OffRoute = true } };
    if (projected)
      state.Plan!.FuelPlan = new()
      {
        EstimatedStationAccess = true,
        StopArrivals = [new(dispatch, delivery, 90, 45)],
      };

    var result = FuelArrivalForecast.Calculate(state);

    if (projected)
    {
      var arrival = Assert.Single(result);
      Assert.Equal(delivery, arrival.StopId);
      Assert.Equal(90, arrival.Gallons);
    }
    else
      Assert.Empty(result);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public void UntimestampedOrFutureFuelCannotCreateAnEstimate(bool future)
  {
    var state = State(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()) with
    {
      FuelUpdatedAt = future ? DateTime.UtcNow.AddHours(1) : null,
    };

    Assert.Empty(FuelArrivalForecast.Calculate(state));
  }

  [Fact]
  public void ReadingAgeAloneDoesNotInvalidateTheLatestKnownFuelLevel()
  {
    var dispatch = Guid.NewGuid();
    var pickup = Guid.NewGuid();
    var delivery = Guid.NewGuid();
    var state = State(dispatch, pickup, delivery) with
    {
      FuelUpdatedAt = DateTime.UtcNow.AddDays(-2),
    };

    var result = FuelArrivalForecast.Calculate(state);

    Assert.Equal(70, result.Single(value => value.StopId == delivery).Gallons);
  }

  [Fact]
  public void ValidManualProjectionDoesNotRequireATelemetryFuelReading()
  {
    var dispatch = Guid.NewGuid();
    var pickup = Guid.NewGuid();
    var delivery = Guid.NewGuid();
    var state = State(dispatch, pickup, delivery) with
    {
      FuelPercent = null,
      FuelUpdatedAt = null,
    };
    state.Plan!.FuelPlan = new()
    {
      ManualStartingFuel = true,
      StopArrivals = [new(dispatch, delivery, 90, 45)],
    };

    var result = Assert.Single(FuelArrivalForecast.Calculate(state));

    Assert.Equal(delivery, result.StopId);
    Assert.Equal(90, result.Gallons);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public void PassedStopsDoNotReceiveTheCurrentTankAsHistoricalArrivalFuel(
    bool savedArrival
  )
  {
    var dispatch = Guid.NewGuid();
    var pickup = Guid.NewGuid();
    var delivery = Guid.NewGuid();
    var state = State(dispatch, pickup, delivery);
    state.Plan!.Tracking.PassedStopIds = [pickup];
    if (savedArrival)
      state.Plan.FuelPlan = new()
      {
        StopArrivals = [new(dispatch, pickup, 100, 50)],
      };

    var result = Assert.Single(FuelArrivalForecast.Calculate(state));

    Assert.Equal(delivery, result.StopId);
    Assert.Equal(70, result.Gallons);
  }

  private static RoutePlanningState State(
    Guid dispatch,
    Guid pickup,
    Guid delivery
  ) =>
    new(
      new() { Mpg = 10, TankGallons = 200 },
      new()
      {
        DispatchId = dispatch,
        FromCurrentPosition = true,
        Stops =
        [
          new(pickup, "Pickup", "", 1, new(1, 1)),
          new(delivery, "Delivery", "", 2, new(2, 2)),
        ],
        Route = new() { Legs = [new(100, 1, []), new(200, 1, [])] },
      },
      new(0, 300, null, 0, false, false, DateTime.UtcNow, new(0.5, 0.5)),
      50,
      DateTime.UtcNow,
      true
    );
}
