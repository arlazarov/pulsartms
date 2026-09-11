using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Models;
using Application.Features.Fleet.Models;
using Domain.Entities.Dispatch;

namespace Server.Tests.Routing;

using Dispatch = global::Domain.Entities.Dispatch.Dispatch;

[Trait("Category", "Routing")]
public class RouteStopTrackerTests
{
  private readonly DateTime now = DateTime.UtcNow;

  [Theory]
  [InlineData(40, -79.6, 90, false)]
  [InlineData(40.1, -79.6, 90, false)]
  [InlineData(40, -79.6, 270, false)]
  [InlineData(40, -80.1, 90, false)]
  public void PositionAndDirectionAloneCannotConfirmPickup(double lat, double lng, int heading, bool passed)
  {
    var (plan, load) = Trip();
    var truck = Location(lat, lng, heading);
    RouteStopTracker.Update(plan, load, truck, now);
    Assert.Equal(passed, plan.Tracking.PassedStopIds.Contains(load.Stops[0].Id));
    Assert.Equal(passed ? load.Stops[1].Id : load.Stops[0].Id, plan.Tracking.NextStopId);
    Assert.All(load.Stops, x => Assert.Null(x.PickedUpAt));
  }

  [Theory]
  [InlineData(true, false)]
  [InlineData(false, true)]
  public void StaleGpsOrFuturePickupDoesNotAdvanceStops(bool stale, bool future)
  {
    var (plan, load) = Trip();
    var truck = Location(40, -79.5, 90);
    if (stale) truck.UpdatedAt = now.AddHours(-1);
    if (future) load.Stops[0].ScheduledDate = DateOnly.FromDateTime(now.AddDays(1));
    RouteStopTracker.Update(plan, load, truck, now);
    Assert.Empty(plan.Tracking.PassedStopIds);
  }

  [Fact]
  public void ArrivalAtDeliveryWaitsForDepartureBeforeAdvancingToNextLoad()
  {
    var (plan, load) = Trip();
    load.Stops[0].PickedUpAt = now.AddHours(-1);
    var truck = Location(40, -79, 90);
    RouteStopTracker.Update(plan, load, truck, now);
    Assert.False(plan.Tracking.AllStopsPassed);
    truck.Longitude = -78.97m;
    truck.UpdatedAt = now.AddMinutes(1);
    RouteStopTracker.Update(plan, load, truck, now.AddMinutes(1));
    Assert.True(plan.Tracking.AllStopsPassed);
    Assert.Null(plan.Tracking.NextStopId);
    Assert.Null(load.Stops[1].DeliveredAt);
  }

  [Fact]
  public void RecentlyObservedParkedTruckKeepsItsLastGpsPosition()
  {
    var (plan, load) = Trip();
    var truck = Location(40, -79.6, 90);
    truck.Speed = 0; truck.EngineState = "off"; truck.UpdatedAt = now.AddHours(-1); truck.ObservedAt = now;
    RouteStopTracker.Update(plan, load, truck, now);
    Assert.Empty(plan.Tracking.PassedStopIds);
  }

  [Fact]
  public void DiscardsLegacyPassedStopsWithoutVisitEvidence()
  {
    var (plan, load) = Trip(true);
    plan.Tracking.PassedStopIds = [load.Stops[0].Id, load.Stops[1].Id];
    RouteStopTracker.Update(plan, load, Location(40, -79.3, 90), now);
    Assert.Empty(plan.Tracking.PassedStopIds);
    Assert.Equal(load.Stops[0].Id, plan.Tracking.NextStopId);
    RouteStopTracker.Update(plan, load, Location(40, -79.51, 90), now);
    Assert.Empty(plan.Tracking.PassedStopIds);
  }

  [Fact]
  public void ReportedDeliveryCompletesEarlierStopsEvenWithoutGps()
  {
    var (plan, load) = Trip();
    load.Stops[1].DeliveredAt = now;
    RouteStopTracker.Update(plan, load, null, now);
    Assert.True(plan.Tracking.AllStopsPassed);
    Assert.Equal(2, plan.Tracking.PassedStopIds.Count);
  }

  [Fact]
  public void BeingBeyondFinalStopCannotCompleteTheLoadWithoutARecordedVisit()
  {
    var (plan, load) = Trip();
    RouteStopTracker.Update(plan, load, Location(40, -78.9, 90), now);
    Assert.False(plan.Tracking.AllStopsPassed);
  }

  private TruckLocation Location(double lat, double lng, int heading) => new()
  { Latitude = (decimal)lat, Longitude = (decimal)lng, Heading = heading, Speed = 60, UpdatedAt = now };

  private (RoutePlan Plan, Dispatch Load) Trip(bool intermediate = false)
  {
    var stops = new List<DispatchStop> { new() { Id = Guid.NewGuid(), Sequence = 1, Job = "Pick Up", Latitude = 40, Longitude = -80, ScheduledDate = DateOnly.FromDateTime(now.AddDays(-1)) } };
    if (intermediate) stops.Add(new() { Id = Guid.NewGuid(), Sequence = 2, Job = "Pick Up", Latitude = 40, Longitude = -79.5m });
    stops.Add(new() { Id = Guid.NewGuid(), Sequence = stops.Count + 1, Job = "Drop Off", Latitude = 40, Longitude = -79 });
    var planned = stops.Select(x => new PlanStop(x.Id, x.Job, "", x.Sequence, new((double)x.Latitude!, (double)x.Longitude!))).ToList();
    var route = new TruckRoute { Miles = 100, Points = planned.Select(x => x.Point).ToList(),
      Legs = planned.Zip(planned.Skip(1), (a, b) => new RouteLeg(100d / (planned.Count - 1), 3600, [a.Point, b.Point])).ToList() };
    return (new() { Route = route, Stops = planned }, new() { Stops = stops, Status = "in_transit" });
  }
}
