using Application.Features.Dispatch.Services;
using Domain.Entities.Execution;
using Domain.Entities.Fleet;
using Domain.Models.Execution;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Integration")]
public sealed class DispatchWorkspaceProjectionTests
{
  [Theory]
  [InlineData(false, false)]
  [InlineData(true, false)]
  [InlineData(true, true)]
  public async Task NativeVisitProjectionMatchesWorkspaceWithoutSourceWrites(
    bool hookConfirmed,
    bool knownTime
  )
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    f.Load.Price = 2500m;
    f.Load.Currency = "USD";
    f.Load.LoadedMiles = 827m;
    f.Load.OrderNumber = "Source order";
    var trucks = new[]
    {
      new Truck
      {
        Id = Guid.NewGuid(),
        ExternalId = "projection-outgoing",
        UnitNumber = "11005",
      },
      new Truck
      {
        Id = Guid.NewGuid(),
        ExternalId = "projection-incoming",
        UnitNumber = "54777",
      },
    };
    var trailer = new Trailer { Id = Guid.NewGuid(), UnitNumber = "9P1175" };
    var outgoingTrip = new Trip { Id = Guid.NewGuid() };
    var incomingTrip = new Trip { Id = Guid.NewGuid() };
    var at = new DateTime(2026, 9, 14, 11, 0, 0, DateTimeKind.Utc);
    var release = new ExecutionTransferVisit
    {
      Id = Guid.NewGuid(),
      TripId = outgoingTrip.Id,
      SourceDispatchStopId = f.Load.Stops[1].Id,
      Operation = "Drop",
      SiteName = "Exchange yard",
      Latitude = 36.9m,
      Longitude = -80.9m,
      ConfirmedBy = f.Actor.Id,
      ActualAt = knownTime ? at : null,
      Revision = 2,
    };
    var receive = new ExecutionTransferVisit
    {
      Id = Guid.NewGuid(),
      TripId = incomingTrip.Id,
      SourceDispatchStopId = f.Load.Stops[2].Id,
      Operation = "Hook",
      SiteName = release.SiteName,
      Latitude = release.Latitude,
      Longitude = release.Longitude,
      ConfirmedBy = hookConfirmed ? f.Actor.Id : null,
      ActualAt = knownTime ? at.AddDays(1) : null,
      Revision = hookConfirmed ? 2 : 1,
    };
    var operation = new DispatchSwitchOperation
    {
      Id = Guid.NewGuid(),
      Status = hookConfirmed ? "completed" : "in_progress",
      IdempotencyKey = Guid.NewGuid(),
    };
    var outgoing = new ExecutionLeg
    {
      Id = Guid.NewGuid(),
      TripId = outgoingTrip.Id,
      TruckId = trucks[0].Id,
      TrailerId = trailer.Id,
      EndSwitchId = operation.Id,
      Status = "completed",
      Stops = ExecutionStopRows.Capture(
        [
          f.Load.Stops[0],
          ExecutionSnapshots.Boundary(release, f.Load.Id, 2, "Loaded"),
        ]
      ),
    };
    var incoming = new ExecutionLeg
    {
      Id = Guid.NewGuid(),
      TripId = incomingTrip.Id,
      TruckId = trucks[1].Id,
      TrailerId = trailer.Id,
      StartSwitchId = operation.Id,
      Status = hookConfirmed ? "active" : "planned",
      Stops = ExecutionStopRows.Capture(
        [
          ExecutionSnapshots.Boundary(receive, f.Load.Id, 0, "Loaded"),
          f.Load.Stops[^1],
        ]
      ),
    };
    var participant = new SwitchParticipant
    {
      Id = Guid.NewGuid(),
      SwitchId = operation.Id,
      DispatchId = f.Load.Id,
      OutgoingLegId = outgoing.Id,
      IncomingLegId = incoming.Id,
      ReleaseVisitId = release.Id,
      ReceiveVisitId = receive.Id,
      ReleasedBy = release.ConfirmedBy,
      ReceivedBy = receive.ConfirmedBy,
      ReleasedAt = release.ActualAt,
      ReceivedAt = receive.ActualAt,
      TransferKind = "drop_hook",
    };
    f.Db.Trucks.AddRange(trucks);
    f.Db.Trailers.Add(trailer);
    f.Db.Trips.AddRange(outgoingTrip, incomingTrip);
    f.Db.DispatchSwitchOperations.Add(operation);
    f.Db.ExecutionLegs.AddRange(outgoing, incoming);
    outgoing.Stops.Single(x => x.Id == release.Id).SourceDispatchStopId =
      release.SourceDispatchStopId;
    incoming.Stops.Single(x => x.Id == receive.Id).SourceDispatchStopId =
      receive.SourceDispatchStopId;
    f.Db.SwitchParticipants.Add(participant);
    f.Db.LoadExecutionLegs.AddRange(
      new LoadExecutionLeg
      {
        Id = Guid.NewGuid(),
        DispatchId = f.Load.Id,
        ExecutionLegId = outgoing.Id,
        Sequence = 1,
      },
      new LoadExecutionLeg
      {
        Id = Guid.NewGuid(),
        DispatchId = f.Load.Id,
        ExecutionLegId = incoming.Id,
        Sequence = 2,
      }
    );
    await f.Db.SaveChangesAsync();
    var before = ExecutionSnapshots.Fingerprint(f.Load);
    var state = await DispatchWorkspaceReader.ReadAsync(
      f.Db,
      f.Load.Id,
      true,
      default
    );
    var response = state!.Response;
    Guid[] ids =
    [
      f.Load.Stops[0].Id,
      release.Id,
      receive.Id,
      f.Load.Stops[^1].Id,
    ];
    Assert.Equal(ids, response.Stops.Select(x => x.Id));
    Assert.Equal(ids, response.Load.Stops.Select(x => x.Id));
    Assert.Equal([1, 2, 3, 4], response.Load.Stops.Select(x => x.Sequence));
    var drop = response.Load.Stops.Single(x => x.Id == release.Id);
    var hook = response.Load.Stops.Single(x => x.Id == receive.Id);
    Assert.True(drop.IsCompleted);
    Assert.Equal(hookConfirmed, hook.IsCompleted);
    Assert.Equal(release.ActualAt, drop.ManualCompletedAt);
    Assert.Equal(receive.ActualAt, hook.ManualCompletedAt);
    Assert.Equal(f.Actor.Id, drop.ManualCompletedBy);
    Assert.Equal("Drop", drop.Job);
    Assert.Equal("Hook", hook.Job);
    Assert.Equal("11005", drop.TruckNumber);
    Assert.Equal("54777", hook.TruckNumber);
    Assert.All(
      response.Stops,
      row =>
        Assert.Equal(row.Id, response.Load.Stops.Single(x => x.Id == row.Id).Id)
    );
    Assert.Equal("confirmed", response.Stops[1].Transfer!.Status);
    Assert.Equal(
      hookConfirmed ? "confirmed" : "planned",
      response.Stops[2].Transfer!.Status
    );
    Assert.Equal(f.Load.Id, response.Load.Id);
    Assert.Null(response.Load.ExecutionLegId);
    Assert.Equal(2500m, response.Load.Price);
    Assert.Equal("USD", response.Load.Currency);
    Assert.Equal(827m, response.Load.LoadedMiles);
    Assert.Equal("Source order", response.Load.OrderNumber);
    Assert.Null(state.EffectiveStops[release.Id].ManualCompletedAt);
    Assert.False(state.EffectiveStops[release.Id].ExecutionCompleted);
    Assert.Equal(before, ExecutionSnapshots.Fingerprint(f.Load));
    Assert.False(f.Db.ChangeTracker.HasChanges());
    Assert.Equal(5, await f.Db.DispatchStops.CountAsync());
    Assert.DoesNotContain(f.Load.Stops, x => x.Id == release.Id);
    Assert.DoesNotContain(f.Load.Stops, x => x.Id == receive.Id);
    var repeated = await DispatchWorkspaceReader.ReadAsync(
      f.Db,
      f.Load.Id,
      true,
      default
    );
    Assert.Equal(
      response.SourceFingerprint,
      repeated!.Response.SourceFingerprint
    );
  }
}
