using Application.Features.Execution.Services;
using Application.Reference;
using Domain.Entities.Execution;
using Domain.Entities.Fleet;
using Domain.Models.Execution;
using Infrastructure.Persistence;
using Server.Tests.Support;

namespace Server.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Integration")]
public sealed class WorkSequenceReaderTests
{
  [Fact]
  public async Task TransferConfirmationChangesEtaInputsWithoutInventingTime()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var (truck, participant) = await SeedAsync(f);
    var before = await f.Planning.EtaInputs.DescribeAsync(truck.Id, default);
    var reader = new TruckItineraryReader(
      f.Db,
      new ExecutionReadScope(f.Db),
      new FleetNames(f.Db),
      new ActiveTransfers(f.Db)
    );
    var snapshot = await reader.ReadAsync(
      truck.Id,
      DateTimeOffset.UtcNow,
      default
    );
    Assert.NotNull(before);
    var blocked = await f.Planning.EtaInputs.PrepareAsync(before, default);
    Assert.Contains("release and receipt", blocked.CurrentUnavailableReason);
    var transfer = Assert.Single(before.Sequence.Transfers);
    Assert.False(transfer.ReleaseConfirmed);
    Assert.False(transfer.ReceiptConfirmed);

    participant.ReleasedBy = participant.ReceivedBy = f.Actor.Id;
    participant.Revision++;
    await f.Db.SaveChangesAsync();

    var after = await f.Planning.EtaInputs.DescribeAsync(truck.Id, default);
    var ready = await f.Planning.EtaInputs.PrepareAsync(after!, default);
    Assert.Empty(after!.Sequence.Issues);
    Assert.Null(ready.CurrentUnavailableReason);
    Assert.NotEqual(before.InputHash, after.InputHash);
    Assert.False(await reader.MatchesAsync(snapshot!, default));
    Assert.Equal(before.GeometryHash, after.GeometryHash);
    Assert.Null(participant.ReleasedAt);
    Assert.Null(participant.ReceivedAt);
    Assert.Equal(0, f.Planning.Hos.ClockCalls);
    Assert.False(f.Db.ChangeTracker.HasChanges());
  }

  [Fact]
  public async Task TimestampsAloneDoNotConfirmAndCancelledTransfersAreExcluded()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var (truck, participant) = await SeedAsync(f);
    participant.ReleasedAt = participant.ReceivedAt = DateTime.UtcNow;
    await f.Db.SaveChangesAsync();
    var selection = Assert.Single(
      await ExecutionWorkReader.ReadAsync(
        f.Db,
        DateOnly.FromDateTime(DateTime.UtcNow),
        new FleetNames(f.Db),
        new ActiveTransfers(f.Db),
        truck.Id,
        false,
        false,
        default
      )
    );
    var evidence = await WorkSequenceReader.ReadAsync(
      f.Db,
      selection.Loads,
      default
    );
    Assert.Equal(new[] { 1, 2 }, evidence.Legs.Select(x => x.Sequence));
    var transfer = Assert.Single(evidence.Transfers);
    Assert.False(transfer.ReleaseConfirmed);
    Assert.False(transfer.ReceiptConfirmed);
    participant.IsCancelled = true;
    participant.Revision++;
    await f.Db.SaveChangesAsync();

    var cancelled = await WorkSequenceReader.ReadAsync(
      f.Db,
      selection.Loads,
      default
    );

    Assert.Empty(cancelled.Transfers);
    Assert.Equal(2, cancelled.Legs.Length);
  }

  private static async Task<(
    Truck Truck,
    SwitchParticipant Participant
  )> SeedAsync(StopCompletionFixture f)
  {
    var truck = new Truck
    {
      Id = Guid.NewGuid(),
      UnitNumber = "Transfer",
      IsActive = true,
    };
    var trip = new Trip { Id = Guid.NewGuid() };
    var release = new ExecutionTransferVisit
    {
      Id = Guid.NewGuid(),
      TripId = trip.Id,
    };
    var receive = new ExecutionTransferVisit
    {
      Id = Guid.NewGuid(),
      TripId = trip.Id,
    };
    var outgoing = new ExecutionLeg
    {
      Id = Guid.NewGuid(),
      Trip = trip,
      TruckId = truck.Id,
      Status = "completed",
      Stops = ExecutionStopRows.Capture(f.Load.Stops),
    };
    var incoming = new ExecutionLeg
    {
      Id = Guid.NewGuid(),
      Trip = trip,
      TruckId = truck.Id,
      Status = "active",
      Stops = ExecutionStopRows.Capture(f.Load.Stops),
    };
    var participant = new SwitchParticipant
    {
      Id = Guid.NewGuid(),
      Switch = new() { Id = Guid.NewGuid(), IdempotencyKey = Guid.NewGuid() },
      DispatchId = f.Load.Id,
      OutgoingLegId = outgoing.Id,
      IncomingLegId = incoming.Id,
      ReleaseVisitId = release.Id,
      ReceiveVisitId = receive.Id,
      Revision = 1,
    };
    f.Db.Trucks.Add(truck);
    outgoing.Stops.AddRange(
      ExecutionStopRows.Capture(
        [ExecutionSnapshots.Boundary(release, f.Load.Id, 1, "Loaded")]
      )
    );
    incoming.Stops.AddRange(
      ExecutionStopRows.Capture(
        [ExecutionSnapshots.Boundary(receive, f.Load.Id, 1, "Loaded")]
      )
    );
    f.Db.LoadExecutionLegs.AddRange(
      new()
      {
        Id = Guid.NewGuid(),
        DispatchId = f.Load.Id,
        ExecutionLeg = outgoing,
        Sequence = 1,
      },
      new()
      {
        Id = Guid.NewGuid(),
        DispatchId = f.Load.Id,
        ExecutionLeg = incoming,
        Sequence = 2,
      }
    );
    f.Db.SwitchParticipants.Add(participant);
    await f.Db.SaveChangesAsync();
    return (truck, participant);
  }
}
