using Application.Features.Execution.Models;
using Application.Features.Routing.Services.Routes;
using Domain.Entities.Execution;
using Domain.Entities.Fleet;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Integration")]
public sealed class TruckPlanningInputsTests
{
  [Fact]
  public async Task FreshWorkBypassesTheDisplayCacheAndRejectsOuterTransactions()
  {
    await using var f = await RouteChoiceFixture.CreateAsync();
    var reader = f.Planning.PlanningInputs;
    var cached = await reader.ReadAsync(f.Truck.Id, default, includeHos: false);
    await f
      .Db.DispatchStops.Where(x => x.Id == f.Load.Stops[0].Id)
      .ExecuteUpdateAsync(s => s.SetProperty(x => x.Notes, "fresh metadata"));

    var fresh = await reader.ReadFreshAsync(f.Truck.Id, default);

    Assert.NotEqual(
      cached!.Itinerary.InputSignature,
      fresh!.Itinerary.InputSignature
    );
    Assert.Null(f.Db.Database.CurrentTransaction);
    await using var transaction = await f.Db.Database.BeginTransactionAsync();
    await Assert.ThrowsAsync<InvalidOperationException>(
      () => reader.ReadFreshAsync(f.Truck.Id, default)
    );
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task NativeDriverClocksCannotFallBackToTheTruckDriver(
    bool assigned
  )
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var previous = new Driver { Id = Guid.NewGuid(), ExternalId = "previous" };
    var receiving = new Driver
    {
      Id = Guid.NewGuid(),
      ExternalId = "receiving",
    };
    var truck = new Truck { Id = Guid.NewGuid(), Driver = previous };
    f.Db.Drivers.Add(receiving);
    f.Db.Trucks.Add(truck);
    f.Load.TruckId = truck.Id;
    f.Db.ExecutionLegs.Add(
      new()
      {
        Id = Guid.NewGuid(),
        TruckId = truck.Id,
        DriverId = assigned ? receiving.Id : null,
        Trip = new() { Id = Guid.NewGuid() },
        Status = "active",
        Stops = ExecutionStopRows.Capture(f.Load.Stops),
        Loads =
        [
          new()
          {
            Id = Guid.NewGuid(),
            DispatchId = f.Load.Id,
            StartVisitId = f.Load.Stops[0].Id,
            EndVisitId = f.Load.Stops[^1].Id,
          },
        ],
      }
    );
    await f.Db.SaveChangesAsync();
    var hos = new PlanningHosProbe(f.Db);
    hos.Clocks["previous"] = new() { DriveMs = 111 };
    hos.Clocks["receiving"] = new() { DriveMs = 222 };
    var reader = Reader(f, hos);

    var result = await reader.ReadAsync(truck.Id, default);

    Assert.Equal(assigned ? 222 : (long?)null, result!.Hos?.DriveMs);
    Assert.Equal(1, hos.Calls);
    Assert.False(f.Db.ChangeTracker.HasChanges());
    var preview = await reader.ReadAsync(truck.Id, default, includeHos: false);
    Assert.Null(preview!.Hos);
    Assert.Equal(1, hos.Calls);
    hos.Clocks["receiving"] = new() { DriveMs = 333 };
    var freshClocks = await reader.ReadAsync(truck.Id, default);
    Assert.Equal(assigned ? 333 : (long?)null, freshClocks!.Hos?.DriveMs);
    Assert.Equal(2, hos.Calls);
  }

  [Theory]
  [InlineData("board")]
  [InlineData("dispatch")]
  [InlineData("execution")]
  public async Task InvalidatingWorkRefreshesTheWholeCapturedSnapshot(
    string group
  )
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var truck = new Truck { Id = Guid.NewGuid() };
    f.Db.Trucks.Add(truck);
    f.Load.TruckId = truck.Id;
    await f.Db.SaveChangesAsync();
    var hos = new PlanningHosProbe(f.Db);
    var reader = Reader(f, hos);
    var first = (await reader.ReadAsync(truck.Id, default, false))!;
    f.Load.Stops[0].Notes = "Updated loading instructions.";
    await f.Db.SaveChangesAsync();
    var cached = (await reader.ReadAsync(truck.Id, default, false))!;
    Assert.Equal(
      first.Itinerary.InputSignature,
      cached.Itinerary.InputSignature
    );
    f.Reads.Invalidate(group);

    var changed = (await reader.ReadAsync(truck.Id, default, false))!;

    Assert.NotEqual(
      first.Itinerary.InputSignature,
      changed.Itinerary.InputSignature
    );
    Assert.Equal(
      "Updated loading instructions.",
      changed.Itinerary.Segments[0].Visits[0].Notes
    );
    Assert.Equal(0, hos.Calls);
  }

  [Fact]
  public async Task BatchReadsRetainCompleteScopeAndFetchClocksOnce()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var first = new Truck
    {
      Id = Guid.NewGuid(),
      UnitNumber = "T1",
      ExternalId = "first",
    };
    var second = new Truck
    {
      Id = Guid.NewGuid(),
      UnitNumber = "T2",
      ExternalId = "second",
    };
    f.Db.Trucks.AddRange(first, second);
    f.Load.TruckId = first.Id;
    f.Load.DeliveryDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
    f.Db.Dispatches.Add(
      new()
      {
        Id = Guid.NewGuid(),
        TruckId = second.Id,
        Status = "planned",
      }
    );
    await f.Db.SaveChangesAsync();
    var hos = new PlanningHosProbe(f.Db);
    var result = await Reader(f, hos)
      .ReadManyAsync([first.Id, second.Id, first.Id, Guid.NewGuid()], default);

    Assert.Equal(2, result.Count);
    Assert.True(Assert.Single(result[first.Id].Itinerary.Segments).IsOverdue);
    Assert.Equal(
      "planned",
      Assert.Single(result[second.Id].Itinerary.Segments).Status
    );
    Assert.Equal(1, hos.Calls);
  }

  [Fact]
  public async Task ADriverChangeDuringClockReadCannotMixAssignments()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var first = new Driver { Id = Guid.NewGuid(), ExternalId = "first" };
    var second = new Driver { Id = Guid.NewGuid(), ExternalId = "second" };
    var truck = new Truck { Id = Guid.NewGuid(), Driver = first };
    f.Db.Drivers.Add(second);
    f.Db.Trucks.Add(truck);
    f.Load.TruckId = truck.Id;
    await f.Db.SaveChangesAsync();
    var hos = new PlanningHosProbe(f.Db);
    hos.Clocks["first"] = new() { DriveMs = 111 };
    hos.Clocks["second"] = new() { DriveMs = 222 };
    hos.BeforeRead = () =>
    {
      truck.DriverId = second.Id;
      f.Db.SaveChanges();
      f.Reads.Invalidate("board");
    };
    var reader = Reader(f, hos);

    var captured = (await reader.ReadAsync(truck.Id, default))!;

    Assert.Equal(first.Id, captured.Itinerary.Resources.DriverId);
    Assert.Equal(111, captured.Hos!.DriveMs);
    hos.BeforeRead = null;
    var next = (await reader.ReadAsync(truck.Id, default))!;
    Assert.Equal(second.Id, next.Itinerary.Resources.DriverId);
    Assert.Equal(222, next.Hos!.DriveMs);
  }

  [Fact]
  public async Task CancellationAndAnEmptyRequestDoNotFetchClocks()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var hos = new PlanningHosProbe(f.Db);
    var reader = Reader(f, hos);
    Assert.Empty(await reader.ReadManyAsync([], default));
    using var cancelled = new CancellationTokenSource();
    cancelled.Cancel();
    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      () => reader.ReadAsync(Guid.NewGuid(), cancelled.Token)
    );
    Assert.Equal(0, hos.Calls);
  }

  private static TruckPlanningInputsReader Reader(
    StopCompletionFixture f,
    PlanningHosProbe hos
  ) =>
    new(
      f.Db,
      f.Planning.Itineraries,
      new ExecutionReadScope(f.Db),
      f.Reads,
      hos,
      new SavedRoutePlanReader(f.Db),
      f.Planning.Profiles
    );
}
