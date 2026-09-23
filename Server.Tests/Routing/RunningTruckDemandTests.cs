using Application.Features.Routing.Background;
using Application.Features.Routing.Services.Routes;
using Domain.Entities.Execution;
using Microsoft.Extensions.DependencyInjection;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Routing;

// Which trucks the background keeps prepared: those with running work.
// Finished work is history and never enters the set; another carrier's work
// is never seen; trucks already driving come first when the set is bounded.
[Trait("Category", "Routing")]
[Trait("Kind", "Integration")]
public sealed class RunningTruckDemandTests
{
  [Fact]
  public async Task RunningWorkIsOpenLegsAndOlderInTransitLoadsOnly()
  {
    await using var f = await PlanningRefreshFixture.CreateAsync();
    var driving = await TruckAsync(f, "driving");
    var planned = await TruckAsync(f, "planned");
    var finished = await TruckAsync(f, "finished");
    var legacy = await TruckAsync(f, "legacy");
    var foreign = await ForeignAsync(f);
    var idle = await TruckAsync(f, "idle");
    Leg(f, planned, "planned");
    Leg(f, driving, "active");
    Leg(f, finished, "completed");
    Leg(f, finished, "cancelled");
    f.Db.Dispatches.Add(
      new Load
      {
        Id = Guid.NewGuid(),
        LoadNumber = 7,
        Status = "in_transit",
        TruckId = legacy,
      }
    );
    f.Db.Dispatches.Add(
      new Load
      {
        Id = Guid.NewGuid(),
        LoadNumber = 8,
        Status = "delivered",
        TruckId = idle,
      }
    );
    await f.Db.SaveChangesAsync();

    var running = await Reader(f).RunningTruckIdsAsync(128, default);

    Assert.Equal(3, running.Count);
    Assert.Equal(new[] { driving, legacy }.Order(), running.Take(2).Order());
    Assert.Equal(planned, running[2]);
    Assert.DoesNotContain(finished, running);
    Assert.DoesNotContain(foreign, running);
    Assert.DoesNotContain(idle, running);
  }

  [Fact]
  public async Task TheBoundKeepsTrucksAlreadyDriving()
  {
    await using var f = await PlanningRefreshFixture.CreateAsync();
    var trucks = new List<(Guid Truck, string Status)>();
    for (var i = 0; i < 6; i++)
    {
      var status = i % 2 == 0 ? "planned" : "active";
      var truck = await TruckAsync(f, $"t{i}");
      Leg(f, truck, status);
      trucks.Add((truck, status));
    }
    await f.Db.SaveChangesAsync();

    var running = await Reader(f).RunningTruckIdsAsync(3, default);

    Assert.Equal(
      trucks.Where(x => x.Status == "active").Select(x => x.Truck).Order(),
      running.Order()
    );
    Assert.True(PlanningSummaryOperation.RunningWorkLimit <= 128);
  }

  // Another carrier's truck driving its own load, written as that carrier.
  private static async Task<Guid> ForeignAsync(PlanningRefreshFixture f)
  {
    var company = Guid.NewGuid();
    await using var scope = f.NewScope();
    var services = scope.ServiceProvider;
    using var serving = services
      .GetRequiredService<Application.Interfaces.ICurrentCompany>()
      .As(company);
    var db =
      services.GetRequiredService<Infrastructure.Persistence.AppDbContext>();
    var truck = new Domain.Entities.Fleet.Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = "foreign",
      UnitNumber = "foreign",
      IsActive = true,
    };
    db.Trucks.Add(truck);
    db.ExecutionLegs.Add(
      new ExecutionLeg
      {
        Id = Guid.NewGuid(),
        Trip = new() { Id = Guid.NewGuid() },
        TruckId = truck.Id,
        Status = "active",
        Revision = 1,
      }
    );
    await db.SaveChangesAsync();
    return truck.Id;
  }

  private static TruckPlanningInputsReader Reader(PlanningRefreshFixture f) =>
    new(f.Db, null!, null!, null!, null!, null!, null!);

  private static async Task<Guid> TruckAsync(
    PlanningRefreshFixture f,
    string unit
  )
  {
    var truck = new Domain.Entities.Fleet.Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = unit,
      UnitNumber = unit,
      IsActive = true,
    };
    f.Db.Trucks.Add(truck);
    await f.Db.SaveChangesAsync();
    return truck.Id;
  }

  private static void Leg(
    PlanningRefreshFixture f,
    Guid truck,
    string status
  ) =>
    f.Db.ExecutionLegs.Add(
      new ExecutionLeg
      {
        Id = Guid.NewGuid(),
        Trip = new() { Id = Guid.NewGuid() },
        TruckId = truck,
        Status = status,
        Revision = 1,
      }
    );
}
