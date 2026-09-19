using Application.Features.Dispatch.Commands;
using Application.Features.Dispatch.Commands.SyncDispatche;
using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Queries;
using Domain.Entities.Fleet;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Integration")]
public sealed class TruckAssignmentTests
{
  [Fact]
  public async Task ConfirmationIsSeparateFromProviderAssignmentsAndSurvivesSync()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var truck = new Truck
    {
      Id = Guid.NewGuid(),
      UnitNumber = "54777",
      IsActive = true,
    };
    f.Db.Trucks.Add(truck);
    await f.Db.SaveChangesAsync();
    var result = await f.AssignmentHandler().Handle(Command(f), default);
    Assert.True(result.Success);
    Assert.Equal(truck.Id, f.Load.PlanningTruckId);
    Assert.Null(f.Load.TruckId);
    Assert.All(f.Load.Stops, s => Assert.Null(s.TruckId));
    Assert.Equal(f.Actor.Id, f.Load.PlanningAssignmentRecordedBy);
    Assert.Equal(1, f.Load.PlanningAssignmentRevision);
    Assert.Equal(f.Load.Stops[1].Id, f.Load.TruckItinerary().Stops[0].Id);

    DispatchMapper.Update(
      f.Load,
      new ExternalDispatch { Status = "in_transit" },
      null,
      null,
      null,
      null,
      DateTime.UtcNow
    );
    await f.Db.SaveChangesAsync();
    Assert.Equal(truck.Id, f.Load.PlanningTruckId);
    var projected = DispatchProjection.Complete(
      await f
        .Db.Dispatches.AsNoTracking()
        .Select(DispatchProjection.Details)
        .SingleAsync()
    );
    Assert.Equal("54777", projected.TruckNumber);
    Assert.True(projected.Stops[0].DriverOnly);
    Assert.False(projected.Stops[0].IsCompleted);
    Assert.Equal(
      409,
      (await f.AssignmentHandler().Handle(Command(f), default)).StatusCode
    );

    var reset = await f.AssignmentHandler()
      .Handle(new(f.Load.Id, new(null, null, 1, null)), default);
    Assert.True(reset.Success);
    Assert.Null(f.Load.PlanningTruckId);
    Assert.Null(f.Load.PlanningFromStopId);
    Assert.Equal(2, f.Load.PlanningAssignmentRevision);
  }

  [Theory]
  [InlineData("Driver", 403)]
  [InlineData(null, 403)]
  [InlineData("Dispatch", 400)]
  public async Task MissingAccessOrUnknownTruckDoesNotWrite(
    string? role,
    int status
  )
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    Assert.Equal(
      status,
      (await f.AssignmentHandler(role).Handle(Command(f), default)).StatusCode
    );
    Assert.Null(f.Load.PlanningTruckId);
    Assert.Equal(0, f.Load.PlanningAssignmentRevision);
  }

  [Fact]
  public async Task SourceAssignmentAndStaleStopIdentityCannotBeOverwritten()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var truck = new Truck
    {
      Id = Guid.NewGuid(),
      UnitNumber = "54777",
      IsActive = true,
    };
    f.Db.Trucks.Add(truck);
    f.Load.Stops[0].TruckId = truck.Id;
    await f.Db.SaveChangesAsync();
    Assert.Equal(
      409,
      (await f.AssignmentHandler().Handle(Command(f), default)).StatusCode
    );
    f.Load.Stops[0].TruckId = null;
    f.Load.TruckNumber = "11007";
    Assert.Equal(
      409,
      (await f.AssignmentHandler().Handle(Command(f), default)).StatusCode
    );
    f.Load.TruckNumber = "";
    var stale = Command(f);
    f.Load.Stops[1].Address = "Changed start";
    await f.Db.SaveChangesAsync();
    Assert.Equal(
      409,
      (await f.AssignmentHandler().Handle(stale, default)).StatusCode
    );
    Assert.Null(f.Load.PlanningTruckId);
  }

  private static SetTruckAssignmentCommand Command(StopCompletionFixture f) =>
    new(
      f.Load.Id,
      new(
        "54777",
        f.Load.Stops[1].Id,
        0,
        f.Command(1, null).Update.CompletionIdentity
      )
    );
}
