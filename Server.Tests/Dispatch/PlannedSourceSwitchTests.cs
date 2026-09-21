using Application.Features.Execution.Commands;
using Application.Features.Execution.Services;
using Domain.Entities.Execution;
using Domain.Entities.Fleet;
using Domain.Models.Execution;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Integration")]
public sealed class PlannedSourceSwitchTests
{
  [Theory]
  [InlineData("assigned")]
  [InlineData("unassigned")]
  public async Task ExplicitPlanWaitsForWorkAndIndependentReceipt(string status)
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    f.Load.Status = status;
    var command = await CommandAsync(f);
    var preview = await f.SwitchPreview().Handle(new(command.Request), default);
    Assert.True(preview.Response!.CanPlan);
    var result = await f.SwitchPlanner().Handle(command, default);
    Assert.True(result.Success, string.Join(";", result.Errors ?? []));
    Assert.True((await f.SwitchPlanner().Handle(command, default)).Success);
    var participant = await f.Db.SwitchParticipants.SingleAsync();
    var legs = await f.Db.ExecutionLegs.ToListAsync();
    Assert.All(
      legs,
      x =>
      {
        Assert.Equal("planned", x.Status);
        Assert.Equal(1, x.Revision);
        Assert.Null(x.StartedAt);
      }
    );
    Assert.Null(participant.ReleasedBy);
    Assert.Null(participant.ReceivedBy);
    Assert.Equal(2, await f.Db.ExecutionLegRevisions.CountAsync());
    var release = new ReleaseSwitchParticipantCommand(
      new(Guid.NewGuid(), participant.SwitchId, participant.Id, 1, 1, 1, null)
    );
    Assert.False((await f.SwitchRelease().Handle(release, default)).Success);

    f.Load.Stops[0].PickedUpAt = f.Clock.GetUtcNow().AddHours(-1).UtcDateTime;
    await ReconcileAsync(f);
    var outgoing = legs.Single(x => x.Id == participant.OutgoingLegId);
    var incoming = legs.Single(x => x.Id == participant.IncomingLegId);
    Assert.Equal("active", outgoing.Status);
    Assert.Equal(2, outgoing.Revision);
    Assert.Equal("planned", incoming.Status);
    Assert.Equal(1, incoming.Revision);
    Assert.Null(participant.ReleasedBy);
    Assert.Null(participant.ReceivedBy);
    Assert.Null(outgoing.StartedAt);
    Assert.Equal(3, await f.Db.ExecutionLegRevisions.CountAsync());
  }

  [Fact]
  public async Task FuturePlanCanReuseBusyTruckButCannotActivateIt()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var command = await CommandAsync(f);
    var trip = new Trip { Id = Guid.NewGuid(), Status = "active" };
    var busy = new ExecutionLeg
    {
      Id = Guid.NewGuid(),
      Trip = trip,
      TripId = trip.Id,
      TruckId = command.Request.Loads[0].Outgoing.TruckId,
      Status = "active",
      Revision = 1,
    };
    f.Db.ExecutionLegs.Add(busy);
    await f.Db.SaveChangesAsync();
    Assert.True(
      (await f.SwitchPreview().Handle(new(command.Request), default))
        .Response!
        .CanPlan
    );
    var result = await f.SwitchPlanner().Handle(command, default);
    Assert.True(result.Success, string.Join(";", result.Errors ?? []));
    f.Load.Stops[0].PickedUpAt = f.Clock.GetUtcNow().AddHours(-1).UtcDateTime;
    await ReconcileAsync(f);
    var outgoingId = result.Response!.Legs[0].OutgoingLegId;
    var outgoing = await f.Db.ExecutionLegs.SingleAsync(x =>
      x.Id == outgoingId
    );
    Assert.Equal("planned", outgoing.Status);
    Assert.Equal(2, outgoing.Revision);
    Assert.Equal(
      f.Load.Stops[0].PickedUpAt,
      outgoing.Stops.OrderBy(x => x.Position).First().PickedUpAt
    );
    Assert.Contains("active assignment", outgoing.SourceReviewReason);
    Assert.Equal(3, await f.Db.ExecutionLegRevisions.CountAsync());
  }

  [Fact]
  public async Task DriverOnlyPrefixCannotBecomeTruckWork()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    f.Load.Stops[0].ManualAction = "Driver start";
    f.Load.Stops[0].ManualStateAfter = "No truck";
    var command = await CommandAsync(f);
    var preview = await f.SwitchPreview().Handle(new(command.Request), default);
    Assert.False(preview.Response!.CanPlan);
    Assert.False((await f.SwitchPlanner().Handle(command, default)).Success);
    Assert.Equal(0, await f.Db.ExecutionLegs.CountAsync());
    Assert.Equal(0, await f.Db.ExecutionLegRevisions.CountAsync());
  }

  private static async Task ReconcileAsync(StopCompletionFixture f)
  {
    await using var transaction = await f.Db.Database.BeginTransactionAsync();
    await ExecutionSourceReconciliation.ApplyAsync(
      f.Db,
      [f.Load],
      f.Clock.GetUtcNow().UtcDateTime,
      default
    );
    await f.Db.SaveChangesAsync();
    await transaction.CommitAsync();
  }

  private static async Task<PlanSwitchCommand> CommandAsync(
    StopCompletionFixture f
  )
  {
    var outgoing = new Truck
    {
      Id = Guid.NewGuid(),
      UnitNumber = "planned-out",
      ExternalId = "planned-out",
      IsActive = true,
    };
    var incoming = new Truck
    {
      Id = Guid.NewGuid(),
      UnitNumber = "planned-in",
      ExternalId = "planned-in",
      IsActive = true,
    };
    f.Db.Trucks.AddRange(outgoing, incoming);
    await f.Db.SaveChangesAsync();
    return new(
      new(
        Guid.NewGuid(),
        "Transfer yard",
        null,
        [
          new(
            f.Load.Id,
            null,
            null,
            ExecutionSnapshots.Fingerprint(f.Load),
            new(outgoing.Id, null, null),
            new(incoming.Id, null, null)
          )
          {
            SplitAfterVisitId = f.Load.Stops[1].Id,
          },
        ]
      )
      {
        Latitude = 35,
        Longitude = -80,
      }
    );
  }
}
