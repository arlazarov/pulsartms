using Application.Features.Dispatch.Commands;
using Application.Features.Dispatch.Commands.SyncDispatche;
using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Queries;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Integration")]
public sealed class StopOperationPersistenceTests
{
  [Fact]
  public async Task OverrideSurvivesSyncAndProjectionPreservesImportedIdentity()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var command = Command(f, "Driver start", "No truck");
    Assert.True((await f.OperationHandler().Handle(command, default)).Success);
    var stop = f.Load.Stops[0];
    Assert.Equal("Pick Up", stop.Job);
    Assert.False(stop.IsCompleted);
    Assert.Equal(f.Actor.Id, stop.OperationRecordedBy);
    var projection = DispatchProjection.Complete(
      await f
        .Db.Dispatches.AsNoTracking()
        .Select(DispatchProjection.Details)
        .SingleAsync()
    );
    Assert.Equal("Driver start", projection.Stops[0].Job);
    Assert.True(projection.Stops[0].DriverOnly);
    Assert.Equal(
      command.Update.StopIdentity,
      projection.Stops[0].CompletionIdentity
    );
    Assert.Equal(
      409,
      (await f.OperationHandler().Handle(command, default)).StatusCode
    );
    DispatchMapper.Update(
      f.Load,
      new ExternalDispatch { Status = "in_transit" },
      null,
      null,
      null,
      null,
      DateTime.UtcNow
    );
    DispatchMapper.UpdateStop(
      stop,
      new ExternalDispatchStop
      {
        Sequence = stop.Sequence,
        Job = stop.Job,
        Name = stop.Name,
        Address = stop.Address,
        City = stop.City,
        Province = stop.Province,
        Country = stop.Country,
      },
      null,
      null,
      null,
      null
    );
    await f.Db.SaveChangesAsync();
    Assert.Equal("Driver start", stop.ManualAction);
    Assert.True(
      (
        await f.OperationHandler()
          .Handle(
            command with
            {
              Update = new(null, null, 1, command.Update.StopIdentity),
            },
            default
          )
      ).Success
    );
    Assert.Null(stop.ManualAction);
    Assert.Equal(2, stop.OperationRevision);
  }

  [Theory]
  [InlineData("Driver", 403)]
  [InlineData(null, 403)]
  public async Task UnauthorizedRolesCannotWrite(string? role, int status)
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    Assert.Equal(
      status,
      (
        await f.OperationHandler(role)
          .Handle(Command(f, "Driver start", "No truck"), default)
      ).StatusCode
    );
    Assert.Equal(0, f.Load.Stops[0].OperationRevision);
  }

  [Fact]
  public async Task InvalidPairAndStaleStopCannotWrite()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    Assert.Equal(
      400,
      (
        await f.OperationHandler()
          .Handle(Command(f, "Driver start", "Loaded"), default)
      ).StatusCode
    );
    var command = Command(f, "Waypoint", "Empty");
    f.Load.Stops[0].Address = "Changed";
    await f.Db.SaveChangesAsync();
    Assert.Equal(
      409,
      (await f.OperationHandler().Handle(command, default)).StatusCode
    );
    Assert.Equal(0, f.Load.Stops[0].OperationRevision);
  }

  private static SetStopOperationCommand Command(
    StopCompletionFixture f,
    string? action,
    string? state
  ) =>
    new(
      f.Load.Id,
      f.Load.Stops[0].Id,
      new(action, state, 0, f.Command(0, null).Update.CompletionIdentity)
    );
}
