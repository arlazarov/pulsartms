using Application.Features.Dispatch.Services;
using Application.Features.Execution.Models;
using Domain.Entities.Dispatch;
using Domain.Entities.Execution;
using Domain.Entities.Fleet;
using Server.Tests.Support;
using DispatchEntity = Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Integration")]
public sealed class WorkspacePersistenceTests
{
  [RequiresPostgresFact]
  public async Task SavedWorkspaceFingerprintMatchesFreshRequest()
  {
    await using var fixture = await PostgresFixture.CreateAsync();
    var db = fixture.Connect();
    var truck = new Truck { Id = Guid.NewGuid(), UnitNumber = "test" };
    var trip = new Trip { Id = Guid.NewGuid() };
    var load = new DispatchEntity
    {
      Id = Guid.NewGuid(),
      LoadNumber = 1,
      Status = "in_transit",
    };
    var stop = new DispatchStop
    {
      Id = Guid.NewGuid(),
      DispatchId = load.Id,
      Sequence = 1,
      Job = "Pick Up",
      ManualCompletionRevision = 1,
      CompletionOverride = false,
      ManualCompletionRecordedAt = new DateTime(
        2026,
        9,
        27,
        10,
        0,
        0,
        DateTimeKind.Utc
      ).AddTicks(1234567),
    };
    load.Stops.Add(stop);
    var leg = new ExecutionLeg
    {
      Id = Guid.NewGuid(),
      TripId = trip.Id,
      TruckId = truck.Id,
      Status = "active",
      Revision = 1,
      Stops = ExecutionStopRows.Capture([stop]),
    };
    db.AddRange(truck, trip, load, leg);
    db.LoadExecutionLegs.Add(
      new()
      {
        Id = Guid.NewGuid(),
        DispatchId = load.Id,
        ExecutionLegId = leg.Id,
        StartVisitId = stop.Id,
        EndVisitId = stop.Id,
        Sequence = 1,
      }
    );
    await db.SaveChangesAsync();
    var saved = await DispatchWorkspaceReader.ReadSavedAsync(
      db,
      load.Id,
      true,
      default
    );
    var fresh = await DispatchWorkspaceReader.ReadAsync(
      fixture.Connect(),
      load.Id,
      true,
      default
    );
    Assert.Equal(
      fresh!.Response.SourceFingerprint,
      saved!.Response.SourceFingerprint
    );
    Assert.Equal(
      1234567,
      stop.ManualCompletionRecordedAt!.Value.Ticks % TimeSpan.TicksPerSecond
    );
    leg.Revision++;
    await db.SaveChangesAsync();
    var changed = await DispatchWorkspaceReader.ReadAsync(
      fixture.Connect(),
      load.Id,
      true,
      default
    );
    Assert.NotEqual(
      saved.Response.SourceFingerprint,
      changed!.Response.SourceFingerprint
    );
  }
}
