using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services.FuelPlanning;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Unit")]
public sealed class FuelManualOccurrencesTests
{
  [Fact]
  public void ResolvesBothDirectionsWithoutChangingThePriceSource()
  {
    var (horizon, price) = Fixture();
    var edits = new List<FuelPlanEditStop>
    {
      new(price.Station.StationId, null, 20, false),
      new(price.Station.StationId, null, 0, true),
    };
    var visits = FuelManualOccurrences.Resolve(
      horizon,
      [price],
      edits,
      new(horizon.Route),
      default
    );
    Assert.Equal(new[] { 0, 1 }, visits.Select(x => x.LegIndex));
    Assert.Equal(
      horizon.Itinerary.Select(x => x.Stop.Id),
      visits.Select(x => x.Station.BeforeStopId)
    );
    Assert.True(visits[0].AlongMiles < visits[1].AlongMiles);
    Assert.Equal(Guid.Empty, price.Station.BeforeStopId);
    Assert.NotSame(visits[0].Station, visits[1].Station);
  }

  [Fact]
  public void ExplicitOccurrenceDoesNotSilentlyMoveToTheReturnJourney()
  {
    var (horizon, price) = Fixture();
    var edits = new List<FuelPlanEditStop>
    {
      new(price.Station.StationId, horizon.Itinerary[1].Stop.Id, 20, false),
      new(price.Station.StationId, horizon.Itinerary[0].Stop.Id, 20, false),
    };
    var error = Assert.Throws<RoutePlanningException>(
      () =>
        FuelManualOccurrences.Resolve(
          horizon,
          [price],
          edits,
          new(horizon.Route),
          default
        )
    );
    Assert.Equal(
      "Fuel stop 2 — LOVES #497 comes before fuel stop 1 on the route. Move it earlier in the plan or remove it.",
      error.Message
    );
  }

  [Fact]
  public void AThirdVisitCannotReuseTheSameStationOccurrence()
  {
    var (horizon, price) = Fixture();
    var edits = Enumerable
      .Repeat(new FuelPlanEditStop(price.Station.StationId, null, 20, false), 3)
      .ToArray();
    var error = Assert.Throws<RoutePlanningException>(
      () =>
        FuelManualOccurrences.Resolve(
          horizon,
          [price],
          edits,
          new(horizon.Route),
          default
        )
    );
    Assert.Equal(
      "Fuel stop 3 — LOVES #497 is already included for this visit. Remove the duplicate stop.",
      error.Message
    );
  }

  [Fact]
  public void MissingPriceUsesTheSavedStationNameAndDoesNotClaimARouteProblem()
  {
    var (horizon, price) = Fixture();
    var error = Assert.Throws<RoutePlanningException>(
      () =>
        FuelManualOccurrences.Resolve(
          horizon,
          [],
          [new(price.Station.StationId, null, 20, false)],
          new(horizon.Route),
          default,
          new Dictionary<Guid, string>
          {
            [price.Station.StationId] = "LOVES #497",
          }
        )
    );
    Assert.Equal(
      "Fuel stop 1 — LOVES #497: no current price is available. Choose another station or remove this stop.",
      error.Message
    );
  }

  [Fact]
  public void MissingPriceWithoutAKnownNameStillIdentifiesTheRow()
  {
    var (horizon, price) = Fixture();
    var error = Assert.Throws<RoutePlanningException>(
      () =>
        FuelManualOccurrences.Resolve(
          horizon,
          [],
          [new(price.Station.StationId, null, 20, false)],
          new(horizon.Route),
          default
        )
    );
    Assert.StartsWith(
      "Fuel stop 1 — Unavailable station: no current price is available.",
      error.Message
    );
  }

  [Fact]
  public void AStationOutsideTheNearbyCorridorDoesNotClaimThatItWasPassed()
  {
    var (horizon, price) = Fixture();
    price.Station.Point = new(42, -79);
    var error = Assert.Throws<RoutePlanningException>(
      () =>
        FuelManualOccurrences.Resolve(
          horizon,
          [price],
          [new(price.Station.StationId, null, 20, false)],
          new(horizon.Route),
          default
        )
    );
    Assert.Equal(
      "Fuel stop 1 — LOVES #497 is not within 40 miles of the remaining route. Choose a station closer to the route or remove this stop.",
      error.Message
    );
  }

  [Fact]
  public void AnUnavailableSelectedVisitDoesNotClaimThatTheWholeStationIsOffRoute()
  {
    var (horizon, price) = Fixture();
    var error = Assert.Throws<RoutePlanningException>(
      () =>
        FuelManualOccurrences.Resolve(
          horizon,
          [price],
          [new(price.Station.StationId, Guid.NewGuid(), 20, false)],
          new(horizon.Route),
          default
        )
    );
    Assert.Equal(
      "Fuel stop 1 — LOVES #497 is not near this part of the trip. Choose a different pickup/delivery position or remove this stop.",
      error.Message
    );
  }

  [Fact]
  public void ReverseOrderNamesBothStationsAndKeepsTheRequestedOrderInvalid()
  {
    var (horizon, price) = Fixture();
    var earlier = price with
    {
      Station = new()
      {
        StationId = Guid.NewGuid(),
        Name = "LOVES #305",
        Point = new(40, -79.75),
      },
    };
    var stopId = horizon.Itinerary[0].Stop.Id;
    var error = Assert.Throws<RoutePlanningException>(
      () =>
        FuelManualOccurrences.Resolve(
          horizon,
          [price, earlier],
          [
            new(price.Station.StationId, stopId, 20, false),
            new(earlier.Station.StationId, stopId, 20, false),
          ],
          new(horizon.Route),
          default
        )
    );
    Assert.Equal(
      "Fuel stop 2 — LOVES #305 comes before LOVES #497 on the route. Move it earlier in the plan or remove it.",
      error.Message
    );
  }

  [Fact]
  public void TheSameRoutePositionDoesNotRecommendAReorderThatCannotWork()
  {
    var (horizon, price) = Fixture();
    var samePosition = price with
    {
      Station = new()
      {
        StationId = Guid.NewGuid(),
        Name = "LOVES #305",
        Point = price.Station.Point,
      },
    };
    var stopId = horizon.Itinerary[0].Stop.Id;
    var error = Assert.Throws<RoutePlanningException>(
      () =>
        FuelManualOccurrences.Resolve(
          horizon,
          [price, samePosition],
          [
            new(price.Station.StationId, stopId, 20, false),
            new(samePosition.Station.StationId, stopId, 20, false),
          ],
          new(horizon.Route),
          default
        )
    );
    Assert.Equal(
      "Fuel stop 2 — LOVES #305 shares the same route position as LOVES #497. Keep only one of these stops.",
      error.Message
    );
  }

  [Fact]
  public void CurrentPriceNamesTakePrecedenceOverSavedNames()
  {
    var (horizon, price) = Fixture();
    price.Station.Point = new(42, -79);
    var error = Assert.Throws<RoutePlanningException>(
      () =>
        FuelManualOccurrences.Resolve(
          horizon,
          [price],
          [new(price.Station.StationId, null, 20, false)],
          new(horizon.Route),
          default,
          new Dictionary<Guid, string>
          {
            [price.Station.StationId] = "Old station name",
          }
        )
    );
    Assert.StartsWith(
      "Fuel stop 1 — LOVES #497 is not within 40 miles",
      error.Message
    );
  }

  [Fact]
  public void AManuallyChosenDearerStationIsNotExcludedByEconomicShortlisting()
  {
    var (horizon, price) = Fixture();
    price.Station.Point = new(40.05, -79.5);
    var dearer = price with { CashUsd = 12, EconomicUsd = 12 };
    var visits = FuelManualOccurrences.Resolve(
      horizon,
      [dearer],
      [new(price.Station.StationId, null, 20, false)],
      new(horizon.Route),
      default
    );
    var visit = Assert.Single(visits);
    Assert.Equal(12, visit.PriceUsd);
    Assert.True(visit.ExtraInMiles > 0);
    Assert.Equal(visit.ExtraInMiles, visit.ExtraOutMiles);
  }

  private static (FuelHorizonResult Horizon, PricedFuelStation Price) Fixture()
  {
    var dispatch = Guid.NewGuid();
    var a = new RoutePoint(40, -80);
    var b = new RoutePoint(40, -79);
    var stops = new List<PlanStop>
    {
      new(Guid.NewGuid(), "Delivery", "Address", 1, b),
      new(Guid.NewGuid(), "Pickup", "Address", 2, a),
    };
    var route = new TruckRoute
    {
      Miles = 200,
      Seconds = 12000,
      Legs = [new(100, 6000, [a, b]), new(100, 6000, [b, a])],
    };
    var horizon = new FuelHorizonResult(route, stops, 1, [dispatch], "", [])
    {
      Itinerary = [new(dispatch, stops[0], 100), new(dispatch, stops[1], 200)],
    };
    return (
      horizon,
      new(
        new()
        {
          StationId = Guid.NewGuid(),
          Name = "LOVES #497",
          Point = new(40, -79.5),
        },
        4,
        4
      )
    );
  }
}
