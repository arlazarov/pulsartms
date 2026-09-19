using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Queries;
using Application.Reference;
using Domain.Entities.Dispatch;
using Domain.Entities.Fleet;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using DispatchEntity = global::Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Integration")]
public sealed class DispatchSyncDeltaTests
{
  [Fact]
  public async Task OneChangedLoadHydratesOnlyThatAggregateAndIdenticalReplayDoesNoDatabaseWork()
  {
    await using var fixture = await DispatchSyncFixture.CreateAsync();
    fixture.Sources.AddRange(
      Enumerable
        .Range(1, 100)
        .Select(number => new ExternalDispatch
        {
          LoadNumber = number,
          Status = "assigned",
          Stops = [new() { Sequence = 1, Job = "Pick Up" }],
        })
    );
    await fixture.Handler.Handle(new(), default);
    fixture.Db.ChangeTracker.Clear();
    fixture.Counter.Reset();
    fixture.Sources[42].Price = 123;
    Assert.True((await fixture.Handler.Handle(new(), default)).Response > 0);
    Assert.Equal(
      43,
      Assert
        .Single(fixture.Db.ChangeTracker.Entries<DispatchEntity>())
        .Entity.LoadNumber
    );
    Assert.Single(fixture.Db.ChangeTracker.Entries<DispatchStop>());
    fixture.Db.ChangeTracker.Clear();
    fixture.Counter.Reset();
    Assert.Equal(0, (await fixture.Handler.Handle(new(), default)).Response);
    Assert.Equal(0, fixture.Counter.Reads);
  }

  [Fact]
  public async Task CatalogGenerationReconcilesMatchingAndManualGenerationPreservesProtectedFields()
  {
    await using var fixture = await DispatchSyncFixture.CreateAsync();
    var source = new ExternalDispatch
    {
      LoadNumber = 10,
      TruckNumber = "101",
      DriverName = "Test Driver",
      Stops =
      [
        new()
        {
          Sequence = 1,
          Job = "Pick Up",
          TruckNumber = "101",
          DriverName = "Test Driver",
        },
      ],
    };
    fixture.Sources.Add(source);
    await fixture.Handler.Handle(new(), default);
    var stop = await fixture.Db.DispatchStops.SingleAsync();
    var stopId = stop.Id;
    var completedAt = DateTime.UtcNow.AddHours(-1);
    stop.ManualCompletedAt = completedAt;
    stop.ManualCompletionRevision = 3;
    stop.ManualAction = "Drop Trailer";
    stop.OperationRevision = 2;
    var truck = new Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = "truck",
      UnitNumber = "101",
      IsActive = true,
    };
    var driver = new Driver
    {
      Id = Guid.NewGuid(),
      ExternalId = "driver",
      Name = "Test Driver",
      IsActive = true,
    };
    fixture.Db.AddRange(truck, driver);
    await fixture.Db.SaveChangesAsync();
    fixture.Reads.Invalidate("fleet-catalog");
    fixture.Reads.Invalidate("dispatch");
    fixture.Db.ChangeTracker.Clear();
    await fixture.Handler.Handle(new(), default);
    stop = await fixture.Db.DispatchStops.SingleAsync();
    Assert.Equal(stopId, stop.Id);
    Assert.Equal(truck.Id, stop.TruckId);
    Assert.Equal(driver.Id, stop.DriverId);
    Assert.Equal(completedAt, stop.ManualCompletedAt);
    Assert.Equal(3, stop.ManualCompletionRevision);
    Assert.Equal("Drop Trailer", stop.ManualAction);
    Assert.Equal(2, stop.OperationRevision);
  }

  [Fact]
  public async Task IdentityPlanningPagesDoNotHydrateDetailsOrInvokeEnrichment()
  {
    await using var fixture = await DispatchSyncFixture.CreateAsync();
    var truck = new Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = "truck",
      UnitNumber = "101",
      IsActive = true,
    };
    fixture.Db.Trucks.Add(truck);
    fixture.Db.Dispatches.Add(
      new()
      {
        Id = Guid.NewGuid(),
        LoadNumber = 10,
        TruckId = truck.Id,
        Status = "assigned",
        Stops =
        [
          new()
          {
            Id = Guid.NewGuid(),
            Sequence = 1,
            Job = "Pick Up",
            Notes = "detail-only-note",
          },
        ],
      }
    );
    await fixture.Db.SaveChangesAsync();
    var handler = new GetDispatchBoardHandler(
      fixture.Db,
      fixture.Reads,
      null!,
      null!,
      null!,
      new FleetNames(fixture.Db),
      NullLogger<GetDispatchBoardHandler>.Instance
    );
    var query = new GetDispatchBoardQuery(
      IncludeHos: false,
      IncludeFinancials: false,
      IncludeEta: false,
      IdentitiesOnly: true
    );
    var first = (await handler.Handle(query, default)).Response!;
    Assert.NotEqual(
      Guid.Empty,
      Assert.Single(Assert.Single(first.Items).Dispatches).Id
    );
    fixture.Counter.Reset();
    var second = (await handler.Handle(query, default)).Response!;
    Assert.Empty(Assert.Single(Assert.Single(second.Items).Dispatches).Stops);
    Assert.Equal(0, fixture.Counter.Reads);
  }
}
