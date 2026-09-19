using Application.Features.Routing.Models;
using Application.Features.Routing.Services.Routes;
using Domain.Entities.Dispatch;
using DispatchEntity = global::Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Unit")]
public sealed class TruckItineraryTests
{
  [Fact]
  public void FirstExplicitTruckStartsTheRouteWithoutCompletingOrRemovingSourceStops()
  {
    var truck = Guid.NewGuid();
    var load = Load();
    load.Stops[1].TruckId = truck;
    var route = load.TruckItinerary();
    Assert.Equal(truck, route.TruckId);
    Assert.Equal(
      load.Stops.Skip(1).Select(s => s.Id),
      route.Stops.Select(s => s.Id)
    );
    Assert.Equal(3, load.Stops.Count);
    Assert.Null(load.TruckId);
    Assert.All(load.Stops, s => Assert.False(s.IsCompleted));
    Assert.Equal(route.Stops, route.TruckItinerary().Stops);
    var hash = RoutePlanningService.HashInputs(load, new TruckRouteProfile());
    load.Stops[0].Address = "Driver's private-car starting point changed";
    Assert.Equal(
      hash,
      RoutePlanningService.HashInputs(load, new TruckRouteProfile())
    );
    load.Stops[1].Address = "Truck starting point changed";
    Assert.NotEqual(
      hash,
      RoutePlanningService.HashInputs(load, new TruckRouteProfile())
    );
  }

  [Fact]
  public void MissingTrailerDoesNotExcludeTruckStopsAndEmptyMiddleAssignmentsAreRetained()
  {
    var load = Load();
    load.Stops[0].TruckId = Guid.NewGuid();
    Assert.Equal(3, load.TruckItinerary().Stops.Count);
    Assert.All(load.TruckItinerary().Stops, s => Assert.Null(s.TrailerId));
  }

  [Fact]
  public void LegacyLoadAssignmentIsRetainedAndEntirelyMissingAssignmentIsNotInvented()
  {
    var load = Load();
    Assert.Null(load.TruckItinerary().TruckId);
    load.TruckId = Guid.NewGuid();
    Assert.Equal(load.TruckId, load.TruckItinerary().TruckId);
    Assert.Equal(3, load.TruckItinerary().Stops.Count);
  }

  [Fact]
  public void ConfirmationUsesExactVisitAndMissingOrConflictingAnchorCannotBecomeAnotherStop()
  {
    var load = Load();
    load.PlanningTruckId = Guid.NewGuid();
    load.PlanningFromStopId = load.Stops[1].Id;
    Assert.Equal(2, load.TruckItinerary().Stops.Count);
    Assert.Equal(load.PlanningTruckId, load.TruckItinerary().TruckId);
    load.Stops[0].TruckId = load.PlanningTruckId;
    Assert.Empty(load.TruckItinerary().Stops);
    load.Stops[0].TruckId = null;
    load.Stops.RemoveAt(1);
    Assert.Empty(load.TruckItinerary().Stops);
  }

  private static DispatchEntity Load() =>
    new()
    {
      Id = Guid.NewGuid(),
      Stops = Enumerable
        .Range(1, 3)
        .Select(i => new DispatchStop
        {
          Id = Guid.NewGuid(),
          Sequence = i,
          Address = "Same address",
        })
        .ToList(),
    };
}
