using System.Text.Json;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services.Deadheads;
using Application.Features.Routing.Services.Routes;
using Domain.Entities.Dispatch;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Routing;

[Trait("Category", "Finance")]
[Trait("Kind", "Unit")]
public sealed class DeadheadTimingTests
{
  [Fact]
  public void SavedDestinationConnectionPreservesTravelTimeAndRejectsChangedPredecessor()
  {
    var truck = Guid.NewGuid();
    var previous = new Load { Id = Guid.NewGuid(), TruckId = truck };
    var current = new Load { Id = Guid.NewGuid(), TruckId = truck };
    var from = new DispatchStop
    {
      Id = Guid.NewGuid(),
      Latitude = 35,
      Longitude = -81,
    };
    var to = new DispatchStop
    {
      Id = Guid.NewGuid(),
      Latitude = 36,
      Longitude = -80,
    };
    var pair = new DeadheadConnection(
      RouteWorkProjection.Capture(previous),
      RouteWorkProjection.Capture(current),
      RouteWorkProjection.CaptureStop(from),
      RouteWorkProjection.CaptureStop(to)
    );
    var profile = new TruckRouteProfile();
    var route = new TruckRoute
    {
      Miles = 100,
      Seconds = 7200,
      Points = [new(35, -81), new(36, -80)],
      Legs = [new(100, 7200, [new(35, -81), new(36, -80)])],
    };
    var saved = new DispatchDeadhead
    {
      DispatchId = current.Id,
      PreviousDispatchId = previous.Id,
      InputHash = pair.Signature(profile),
      Miles = 100,
      RouteJson = JsonSerializer.Serialize(route, RoutePlanningService.Json),
    };
    for (var i = 0; i < 3; i++)
    {
      var reused = Assert.IsType<TruckRoute>(pair.ReadRoute(saved, profile));
      Assert.Equal(7200, reused.Seconds);
      Assert.Equal(7200, Assert.Single(reused.Legs).Seconds);
    }
    Assert.Null(
      (
        pair with
        {
          Previous = pair.Previous with { Id = Guid.NewGuid() },
        }
      ).ReadRoute(saved, profile)
    );
    to.Longitude = -79;
    Assert.NotNull(pair.ReadRoute(saved, profile));
    Assert.Null(
      (pair with { To = RouteWorkProjection.CaptureStop(to) }).ReadRoute(
        saved,
        profile
      )
    );
  }
}
