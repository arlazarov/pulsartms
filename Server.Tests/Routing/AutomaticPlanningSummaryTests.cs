using Application.Features.Dispatch.Queries;

namespace Server.Tests.Routing;

public partial class AutomaticPlanningTests
{
  [Fact]
  public async Task BoardSummariesShareTheBoardReadAndNeverReturnRoadCoordinatesOrCallRouting()
  {
    await using var fixture = await Fixture.CreateAsync(pickedUp: true);
    var full = await fixture.Service.ForTruckAsync(fixture.Truck.Id, default);
    var providerCalls = fixture.Router.Calls;
    var boardCalls = fixture.Sender.BoardCalls;
    var summary = Assert.Single(
      await fixture.Reader.ForBoardAsync(
        new GetDispatchBoardQuery(TruckId: fixture.Truck.Id),
        default
      )
    );
    Assert.Equal(boardCalls + 1, fixture.Sender.BoardCalls);
    Assert.Equal(providerCalls, fixture.Router.Calls);
    Assert.Equal(full.TruckId, summary.TruckId);
    Assert.Equal(full.DispatchId, summary.DispatchId);
    var plan = summary.State!.Plan!;
    Assert.True(plan.GeometryOmitted);
    Assert.Equal(
      full.State!.Plan!.OriginalPlannedMiles,
      plan.OriginalPlannedMiles
    );
    Assert.Equal(
      full.State.Progress!.RemainingMiles,
      summary.State.Progress!.RemainingMiles
    );
    Assert.Empty(plan.Route.Points);
    Assert.All(plan.Route.Legs, leg => Assert.Empty(leg.Points));
    if (plan.ReferenceRoute is { } reference)
    {
      Assert.Empty(reference.Points);
      Assert.All(reference.Legs, leg => Assert.Empty(leg.Points));
    }
    var subsequentMapRead = await fixture.Reader.ForTruckAsync(
      fixture.Truck.Id,
      default
    );
    Assert.False(subsequentMapRead.State!.Plan!.GeometryOmitted);
    Assert.Contains(
      subsequentMapRead.State.Plan.Route.Legs,
      leg => leg.Points.Count > 0
    );
  }

  [Fact]
  public async Task SummaryWithNoSavedRouteRetainsAuthoritativeIdentityWithoutBuildingRoads()
  {
    await using var fixture = await Fixture.CreateAsync();
    var summary = Assert.Single(
      await fixture.Reader.ForBoardAsync(
        new GetDispatchBoardQuery(TruckId: fixture.Truck.Id),
        default
      )
    );
    Assert.Equal(fixture.Load.Id, summary.DispatchId);
    Assert.Null(summary.State!.Plan);
    Assert.Equal(0, fixture.Router.Calls);
    Assert.Equal(1, fixture.Sender.BoardCalls);
  }
}
