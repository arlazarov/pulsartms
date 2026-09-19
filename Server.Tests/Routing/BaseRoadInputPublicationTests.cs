using Application.Features.Execution.Services;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Services.Routes;
using Microsoft.EntityFrameworkCore;
using Server.Tests.Support;

namespace Server.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Integration")]
public sealed class BaseRoadInputPublicationTests
{
  [Theory]
  [InlineData(false, false)]
  [InlineData(true, false)]
  [InlineData(false, true)]
  [InlineData(true, true)]
  public async Task CallerMutationCannotChangeCapturedWorkOrDimensions(
    bool native,
    bool captured
  )
  {
    await using var f = await RouteChoiceFixture.CreateAsync();
    var load = f.Load;
    var leg = native ? await f.AddExecutionAsync("completed") : null;
    var profile = await f.Planning.Profiles.GetAsync(f.Truck.Id, default);
    var work = leg is null
      ? RouteWorkProjection.Capture(load.TruckItinerary())
      : RouteWorkProjection.Capture(load, leg, load.Stops);
    var expected = BaseRouteService.Signature(work, profile);
    f.Routing.BeforeCalculate = () =>
    {
      load.Stops[^1].Latitude = 46;
      load.Stops.Clear();
      profile.HeightFeet = 16;
      return Task.CompletedTask;
    };
    var route =
      captured || native
        ? await f.Planning.BaseRoutes.EnsureAsync(work, profile, default)
        : await f.Planning.BaseRoutes.EnsureAsync(load, profile, default);
    Assert.Equal(43, route.Legs[^1].Points[^1].Latitude);
    Assert.Equal(
      expected,
      (await f.Db.DispatchBaseRoutes.SingleAsync()).InputHash
    );
  }

  [Theory]
  [InlineData("stop")]
  [InlineData("assignment")]
  [InlineData("choice")]
  [InlineData("accepted")]
  public async Task StandaloneInputChangeRejectsThePendingRoad(string change)
  {
    await using var f = await RouteChoiceFixture.CreateAsync();
    var profile = await f.Planning.Profiles.GetAsync(f.Truck.Id, default);
    f.Routing.BeforeCalculate = async () =>
    {
      if (change == "stop")
        await f
          .Db.DispatchStops.Where(x => x.Id == f.Load.Stops[1].Id)
          .ExecuteUpdateAsync(s => s.SetProperty(x => x.Latitude, 45m));
      else if (change == "assignment")
        await f.Db.Dispatches.ExecuteUpdateAsync(s =>
          s.SetProperty(x => x.PlanningAssignmentRevision, 1)
        );
      else if (change == "choice")
        await f.Db.Dispatches.ExecuteUpdateAsync(s =>
          s.SetProperty(x => x.RouteChoiceRevision, 1)
        );
      else
        await f.AddExecutionAsync("active");
    };
    var error = await Assert.ThrowsAsync<RoutePlanningException>(
      () => f.Planning.BaseRoutes.EnsureAsync(f.Load, profile, default)
    );
    Assert.Contains("base route work changed", error.Message);
    Assert.False(await f.Db.DispatchBaseRoutes.AnyAsync());
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task ConcurrentRoadPublicationCannotBeOverwritten(bool existing)
  {
    await using var f = await RouteChoiceFixture.CreateAsync();
    var profile = await f.Planning.Profiles.GetAsync(f.Truck.Id, default);
    if (existing)
    {
      await f.Planning.BaseRoutes.EnsureAsync(f.Load, profile, default);
      await f.Db.DispatchBaseRoutes.ExecuteUpdateAsync(s =>
        s.SetProperty(x => x.InputHash, "stale")
      );
    }
    f.Routing.BeforeCalculate = async () =>
    {
      if (existing)
        await f.Db.DispatchBaseRoutes.ExecuteUpdateAsync(s =>
          s.SetProperty(x => x.InputHash, "concurrent")
        );
      else
      {
        f.Db.DispatchBaseRoutes.Add(
          new()
          {
            Id = Guid.NewGuid(),
            DispatchId = f.Load.Id,
            InputHash = "concurrent",
          }
        );
        await f.Db.SaveChangesAsync();
      }
    };
    var error = await Assert.ThrowsAsync<RoutePlanningException>(
      () => f.Planning.BaseRoutes.EnsureAsync(f.Load, profile, default)
    );
    Assert.Contains("saved base route changed", error.Message);
    Assert.Equal(
      "concurrent",
      (await f.Db.DispatchBaseRoutes.AsNoTracking().SingleAsync()).InputHash
    );
  }

  [Fact]
  public async Task NumberOnlyAssignmentRetainsItsResolvedTruckIdentity()
  {
    await using var f = await RouteChoiceFixture.CreateAsync();
    await f.Db.Trucks.ExecuteUpdateAsync(s =>
      s.SetProperty(x => x.UnitNumber, "17")
    );
    await f.Db.Dispatches.ExecuteUpdateAsync(s =>
      s.SetProperty(x => x.TruckId, (Guid?)null)
        .SetProperty(x => x.TruckNumber, "17")
    );
    f.Load.TruckNumber = "17";
    var profile = await f.Planning.Profiles.GetAsync(f.Truck.Id, default);
    await f.Planning.BaseRoutes.EnsureAsync(f.Load, profile, default);
    Assert.Single(await f.Db.DispatchBaseRoutes.ToListAsync());
  }
}
