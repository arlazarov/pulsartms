using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services.Routes;

namespace Server.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Unit")]
public sealed class RouteDisplayIsolationTests
{
  [Theory]
  [InlineData("http")]
  [InlineData("invalid")]
  [InlineData("timeout")]
  [InlineData("geometry")]
  public async Task OptionalContextFailureRetainsReferenceAndOperationalRoad(
    string failure
  )
  {
    var pickup = new PlanStop(Guid.NewGuid(), "", "", 1, new(40, -80));
    var delivery = new PlanStop(Guid.NewGuid(), "", "", 2, new(40, -79));
    var reference = Road(pickup.Point, delivery.Point);
    var remaining = Road(new(41, -80), delivery.Point);
    var result = await RouteDisplayReference.ReconnectAsync(
      reference,
      [pickup, delivery],
      [delivery],
      remaining,
      new(),
      new FailingRouter(failure),
      default
    );
    Assert.Same(reference, result);
    Assert.Equal(new RoutePoint(41, -80), remaining.Legs[0].Points[0]);
    Assert.Equal(100, remaining.Miles);
  }

  [Fact]
  public async Task UserCancellationIsNotHiddenByOptionalContext()
  {
    var pickup = new PlanStop(Guid.NewGuid(), "", "", 1, new(40, -80));
    var delivery = new PlanStop(Guid.NewGuid(), "", "", 2, new(40, -79));
    using var cancelled = new CancellationTokenSource();
    cancelled.Cancel();
    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      () =>
        RouteDisplayReference.ReconnectAsync(
          Road(pickup.Point, delivery.Point),
          [pickup, delivery],
          [delivery],
          Road(new(41, -80), delivery.Point),
          new(),
          new FailingRouter("timeout"),
          cancelled.Token
        )
    );
  }

  [Theory]
  [InlineData(null, "Empty")]
  [InlineData("Loaded", "Loaded")]
  [InlineData("Empty", "Empty")]
  [InlineData("Unknown", "Unknown")]
  public void ServerClassifiesApproachUsingPrecedingAcceptedStop(
    string? previousState,
    string expected
  )
  {
    var pickup = new PlanStop(Guid.NewGuid(), "", "", 1, new(40, -80))
    {
      Job = "Pick Up",
      StateAfter = "Loaded",
    };
    var drop = new PlanStop(Guid.NewGuid(), "", "", 2, new(40, -79));
    var plan = new RoutePlan
    {
      FromCurrentPosition = true,
      Stops = [pickup, drop],
      Route = new() { Legs = [new(10, 10, []), new(20, 20, [])] },
      ReferenceStops = previousState is null
        ? null
        :
        [
          new(Guid.NewGuid(), "", "", 0, new(40, -81))
          {
            StateAfter = previousState,
          },
          pickup,
          drop,
        ],
    };
    var segments = RouteSegmentClassification.Read(plan);
    Assert.Equal(expected, segments[0].CargoState);
    Assert.Equal("Loaded", segments[1].CargoState);
    Assert.All(segments, x => Assert.Equal("EstimatedRoad", x.GeometrySource));
  }

  [Fact]
  public void LaterPickupWithoutPredecessorEvidenceKeepsUnknownCargo()
  {
    var plan = new RoutePlan
    {
      FromCurrentPosition = true,
      Stops =
      [
        new(Guid.NewGuid(), "", "", 3, new(40, -80))
        {
          Job = "Pick Up",
          StateAfter = "Loaded",
        },
      ],
      Route = new() { Legs = [new(10, 10, [])] },
    };
    Assert.Equal("Unknown", Assert.Single(plan.Segments).CargoState);
  }

  private static TruckRoute Road(RoutePoint start, RoutePoint end) =>
    new()
    {
      Miles = 100,
      Seconds = 100,
      Legs = [new(100, 100, [start, end])],
    };

  private sealed class FailingRouter(string failure) : IRoutingProvider
  {
    public bool IsConfigured => true;

    public Task<RoutePoint> GeocodeAsync(
      string address,
      CancellationToken ct
    ) => throw new NotSupportedException();

    public Task<TruckRoute> CalculateAsync(
      IReadOnlyList<RoutePoint> points,
      TruckRouteProfile profile,
      CancellationToken ct
    ) =>
      failure switch
      {
        "http" => throw new HttpRequestException(),
        "invalid" => throw new RoutePlanningException("Unavailable"),
        "timeout" => throw new OperationCanceledException(ct),
        _ => Task.FromResult(Road(new(35, -90), new(36, -89))),
      };
  }
}
