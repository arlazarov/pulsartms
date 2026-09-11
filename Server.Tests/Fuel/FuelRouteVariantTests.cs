using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Models;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Services.FuelPlanning;
namespace Server.Tests.Fuel;
[Trait("Category", "Fuel")]
[Trait("Kind", "Unit")]
public class FuelRouteVariantTests
{
  [Fact]
  public void CheckedRouteChargesItsActualTimeOnceWithoutMutatingEstimatedCandidateMetadata()
  {
    var start = new RoutePoint(40, -80);
    var stop = new PlanStop(Guid.NewGuid(), "Delivery", "Warehouse", 1, new(40, -79));
    var source = new FuelPlanStop
    {
      Number = 2, VisitKey = "return", DispatchId = Guid.NewGuid(), BeforeStopId = stop.Id,
      CashUsdPerGallon = 3, EconomicUsdPerGallon = 2.9, CurrentRouteMile = 50,
      StationId = Guid.NewGuid(), Name = "Fuel", Address = "100 Fuel Road", Point = new(40, -79.5),
      MilesAhead = 52, ArrivalGallons = 12, BuyGallons = 40, DepartureGallons = 52, FillToTarget = true,
      YourPrice = 3, EconomicPrice = 2.9, Currency = "USD", Unit = "US gal", DetourMiles = 4,
      DetourMinutes = 60, PriceDate = new(2026, 9, 9)
    };
    var original = System.Text.Json.JsonSerializer.Serialize(source);
    var candidate = new FuelCandidate(source, 50, 2, 2, 3, 3) { LegIndex = 0, EntryMiles = 48, ExitMiles = 52 };
    var route = new TruckRoute { Miles = 100, Seconds = 7200, Points = [start, source.Point, stop.Point],
      Legs = [new(50, 3600, [start, source.Point]), new(50, 3600, [source.Point, stop.Point])] };
    var checkedRoute = FuelRouteVariant.Collapse(route, [new(start), new(source.Point, Fuel: candidate), new(stop.Point, Stop: stop)]);
    var checkedCandidate = Assert.Single(checkedRoute.Stations);
    Assert.NotSame(candidate, checkedCandidate);
    Assert.NotSame(source, checkedCandidate.Station);
    Assert.Equal(0, checkedCandidate.Station.DetourMinutes);
    Assert.Equal(0, checkedCandidate.ExtraInMiles);
    Assert.Equal(0, checkedCandidate.ExtraOutMiles);
    Assert.Null(checkedCandidate.EntryMiles);
    Assert.Null(checkedCandidate.ExitMiles);
    Assert.Equal(candidate.VisitKey, checkedCandidate.VisitKey);
    Assert.Equal(original, System.Text.Json.JsonSerializer.Serialize(source));
    var expected = System.Text.Json.JsonSerializer.SerializeToNode(source)!;
    expected[nameof(FuelPlanStop.DetourMinutes)] = 0;
    Assert.Equal(expected.ToJsonString(), System.Text.Json.JsonSerializer.Serialize(checkedCandidate.Station));

    var profile = new TruckRouteProfile { Confirmed = true, TankGallons = 100, Mpg = 5,
      ReserveGallons = 10, FillPercent = 100, StopCostUsd = 20, DriverHourlyCostUsd = 35 };
    var plan = FuelOptimizer.Optimize(checkedRoute.Route.Miles, 20, profile, checkedRoute.Stations, 1, false);
    var impact = new FuelScheduleImpact(DateTime.UnixEpoch, true, true, false, false, 60, 0, [], null);
    var delayCost = FuelScheduleRanking.DelayCost(impact, 0, (checkedRoute.Route.Seconds - 3600) / 60, profile.DriverHourlyCostUsd);
    Assert.Equal(25, Assert.Single(plan.Stops).BuyGallons);
    Assert.Equal(95, plan.EconomicCostUsd);
    Assert.Equal(130, plan.EconomicCostUsd + delayCost);
    Assert.Equal(60, source.DetourMinutes);
  }

  [Fact]
  public void KeepsPickupBeforeDeliveryAndDoesNotReturnToOldRoad()
  {
    var start = new RoutePoint(40, -80);
    var pickup = new PlanStop(Guid.NewGuid(), "Pickup", "", 1, new(40, -79));
    var delivery = new PlanStop(Guid.NewGuid(), "Delivery", "", 2, new(40, -78));
    var reference = new RouteGeometry(new() { Legs = [new(100, 6000, [start, pickup.Point]), new(100, 6000, [pickup.Point, delivery.Point])] });
    var station = new FuelCandidate(new() { StationId = Guid.NewGuid(), Point = new(39.9, -78.5) }, 150, 0, 0, 3, 3);
    var points = FuelRouteVariant.Waypoints(start, [pickup, delivery], [station], reference, 0);
    Assert.Equal(new[] { start, pickup.Point, station.Station.Point, delivery.Point }, points.Select(x => x.Point));
    var route = new TruckRoute { Miles = 202, Seconds = 12120, Points = points.Select(x => x.Point).ToList(),
      Legs = [new(100, 6000, [start, pickup.Point]), new(49, 2940, [pickup.Point, station.Station.Point]), new(53, 3180, [station.Station.Point, delivery.Point])] };
    var result = FuelRouteVariant.Collapse(route, points);
    Assert.Equal(2, result.Route.Legs.Count);
    Assert.Equal(102, result.Route.Legs[1].Miles);
    Assert.Contains(station.Station.Point, result.Route.Legs[1].Points);
    Assert.Equal(149, Assert.Single(result.Stations).AlongMiles);
    Assert.Equal(202, new RouteGeometry(result.Route).Miles, 5);
  }

  [Theory]
  [InlineData(58)]
  [InlineData(62)]
  [InlineData(-1)]
  [InlineData(double.NaN)]
  public void InvalidTimingCannotBeHiddenByJoiningOrCollapsing(double seconds)
  {
    var start = new RoutePoint(40, -80);
    var stop = new PlanStop(Guid.NewGuid(), "Delivery", "", 1, new(40, -79));
    var route = new TruckRoute { Miles = 100, Seconds = seconds, Points = [start, stop.Point], Legs = [new(100, 60, [start, stop.Point])] };
    var valid = new TruckRoute { Miles = 100, Seconds = 60, Points = [stop.Point, new(40, -78)], Legs = [new(100, 60, [stop.Point, new(40, -78)])] };
    Assert.Throws<RoutePlanningException>(() => FuelRouteVariant.Collapse(route, [new(start), new(stop.Point, Stop: stop)]));
    Assert.Throws<RoutePlanningException>(() => FuelHorizon.Join(route, valid));
    Assert.Throws<RoutePlanningException>(() => FuelHorizon.Join(valid, route));
  }
}
