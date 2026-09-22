using Application.Features.Routing.Services.Routes;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Routing;

public partial class AutomaticPlanningTests
{
  [Fact]
  public async Task DuplicateAndOutOfOrderGpsDoNotRewriteProgress()
  {
    await using var f = await Fixture.CreateAsync(pickedUp: true);
    await f.Service.ForTruckAsync(f.Truck.Id, default);
    var first = await f.Db.DispatchRoutePlans.AsNoTracking().SingleAsync();
    var time = f.Location.UpdatedAt;
    var repeat = await f.Service.ForTruckAsync(f.Truck.Id, default);
    var second = await f.Db.DispatchRoutePlans.AsNoTracking().SingleAsync();
    Assert.Equal(first.PlanJson, second.PlanJson);
    Assert.Equal(first.GeometryRevision, second.GeometryRevision);
    f.Location.UpdatedAt = time.AddSeconds(-5);
    await f.Service.ForTruckAsync(f.Truck.Id, default);
    var third = await f.Db.DispatchRoutePlans.AsNoTracking().SingleAsync();
    var restored = RoutePlanStorage.Read(
      (await RoutePlanStorage.LoadAsync(f.Db, third, default))!
    )!;
    Assert.Equal(time, restored.Tracking.LastObservationAt);
    Assert.Equal(first.GeometryRevision, third.GeometryRevision);
  }
}
