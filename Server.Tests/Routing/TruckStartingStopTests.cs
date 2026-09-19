using Application.Features.Dispatch.Queries;
using Domain.Entities.Dispatch;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Routing;

public partial class AutomaticPlanningTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task AutomaticAndConfirmedStartExcludeDriverTravelFromBoardRoadAndFuel(
    bool confirmed
  )
  {
    await using var f = await Fixture.CreateAsync();
    f.Load.Truck = null;
    f.Load.TruckId = null;
    var personal = f.Load.Stops[0];
    personal.Latitude = 45;
    personal.Job = "Pick Up";
    f.Load.Stops[1].Job = "Pick Up";
    f.Load.Stops.Add(
      new DispatchStop
      {
        Id = Guid.NewGuid(),
        Sequence = 3,
        Job = "Drop Off",
        Latitude = 40,
        Longitude = -78,
      }
    );
    f.Db.DispatchStops.Add(f.Load.Stops[^1]);
    if (confirmed)
    {
      f.Load.PlanningTruckId = f.Truck.Id;
      f.Load.PlanningFromStopId = f.Load.Stops[1].Id;
    }
    else
      f.Load.Stops[1].TruckId = f.Truck.Id;
    await f.Db.SaveChangesAsync();
    var board = await f.Services.Board.Handle(
      new(
        TruckId: f.Truck.Id,
        IncludeHos: false,
        IncludeFinancials: false,
        IncludeEta: false
      ),
      default
    );
    var load = Assert.Single(Assert.Single(board.Response!.Items).Dispatches);
    Assert.Equal(3, load.Stops.Count);
    Assert.True(load.Stops[0].DriverOnly);
    Assert.False(load.Stops[0].IsCompleted);
    Assert.All(load.Stops.Skip(1), s => Assert.False(s.DriverOnly));
    var result = await f.Service.ForTruckAsync(f.Truck.Id, default);
    Assert.Null(result.Message);
    Assert.Equal(
      f.Load.Stops.Skip(1).Select(s => s.Id),
      result.State!.Plan!.Stops.Select(s => s.Id)
    );
    Assert.DoesNotContain(
      f.Router.Requests.SelectMany(r => r),
      p => p.Latitude == 45
    );
    Assert.All(
      f.Router.Requests,
      r => Assert.Contains(r, p => p.Longitude == -79)
    );
    f.Location.FuelPercent = 40;
    f.Location.FuelUpdatedAt = DateTime.UtcNow;
    var calls = f.Router.Calls;
    var fuel = await f.Service.RecalculateFuelAsync(f.Load.Id, default);
    Assert.DoesNotContain(
      fuel.State!.Plan!.FuelPlan!.StopArrivals,
      s => s.StopId == personal.Id
    );
    Assert.Equal(calls, f.Router.Calls);
  }
}
