using Application.Features.Dispatch.Activity;
using Application.Features.Dispatch.Commands.SyncDispatche;
using Application.Features.Dispatch.Models;
using Application.Features.Execution.Models;
using Domain.Entities.Dispatch;
using Domain.Entities.Execution;
using Domain.Entities.Fleet;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Server.Tests.Support;
using DispatchEntity = Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Integration")]
public sealed class DispatchActivityTests
{
  [Fact]
  public async Task AddIsInternalServerAttributedAndIdempotent()
  {
    await using var f = await DispatchActivityFixture.CreateAsync();
    var command = f.Command(attention: true);
    var result = await f.Add().Handle(command, default);
    Assert.True(result.Success);
    Assert.Equal(f.Actor.Id, result.Response!.ActorId);
    Assert.Equal(f.Actor.Name, result.Response.ActorName);
    Assert.Equal(f.Clock.GetUtcNow().UtcDateTime, result.Response.RecordedAt);
    Assert.Equal(f.Load.Stops[0].Id, result.Response.StopId);
    Assert.Equal("in_transit", f.Load.Status);
    Assert.False(f.Load.Stops[0].IsCompleted);
    var repeat = await f.Add().Handle(command, default);
    Assert.Equal(result.Response.Id, repeat.Response!.Id);
    Assert.Equal(1, await f.Db.DispatchActivityEntries.CountAsync());
    Assert.Equal(
      1,
      (await f.Db.DispatchActivityThreads.SingleAsync()).Revision
    );
    Assert.Equal(
      409,
      (
        await f.Add()
          .Handle(
            command with
            {
              Update = command.Update with { Text = "Different content" },
            },
            default
          )
      ).StatusCode
    );
  }

  [Fact]
  public async Task ResolvePreservesOriginalAndRejectsStaleOrWrongLoad()
  {
    await using var f = await DispatchActivityFixture.CreateAsync();
    var original = (
      await f.Add().Handle(f.Command(attention: true), default)
    ).Response!;
    var command = new ResolveDispatchActivityCommand(
      f.Load.Id,
      original.Id,
      new(Guid.NewGuid(), original.Revision)
    );
    var resolved = await f.Resolve().Handle(command, default);
    Assert.True(resolved.Success);
    Assert.Equal(original.Text, resolved.Response!.Text);
    Assert.Equal(original.RecordedAt, resolved.Response.RecordedAt);
    Assert.Equal(original.ActorId, resolved.Response.ActorId);
    Assert.Equal(f.Actor.Id, resolved.Response.ResolvedBy);
    Assert.NotNull(resolved.Response.ResolvedAt);
    Assert.True((await f.Resolve().Handle(command, default)).Success);
    Assert.Equal(
      409,
      (
        await f.Resolve()
          .Handle(
            command with
            {
              Update = new(Guid.NewGuid(), original.Revision),
            },
            default
          )
      ).StatusCode
    );
    Assert.Equal(
      404,
      (
        await f.Resolve()
          .Handle(command with { DispatchId = Guid.NewGuid() }, default)
      ).StatusCode
    );
  }

  [Fact]
  public async Task OriginalEntryFieldsCannotBeRewrittenAfterSaving()
  {
    await using var f = await DispatchActivityFixture.CreateAsync();
    var saved = (
      await f.Add().Handle(f.Command(attention: true), default)
    ).Response!;
    var tracked = await f.Db.DispatchActivityEntries.SingleAsync();
    var mutable = new HashSet<string>
    {
      nameof(DispatchActivityEntry.Revision),
      nameof(DispatchActivityEntry.ResolveOperationId),
      nameof(DispatchActivityEntry.ResolvedBy),
      nameof(DispatchActivityEntry.ResolvedByName),
      nameof(DispatchActivityEntry.ResolvedAt),
    };
    foreach (var property in f.Db.Entry(tracked).Metadata.GetProperties())
      Assert.Equal(
        mutable.Contains(property.Name)
          ? PropertySaveBehavior.Save
          : PropertySaveBehavior.Throw,
        property.GetAfterSaveBehavior()
      );
    tracked.Text = "An accidental replacement";
    await Assert.ThrowsAsync<InvalidOperationException>(
      () => f.Db.SaveChangesAsync()
    );
    f.Db.ChangeTracker.Clear();
    var retained = await f.Db.DispatchActivityEntries.SingleAsync();
    Assert.Equal(saved.Text, retained.Text);
    Assert.Equal(saved.ActorId, retained.ActorId);
    Assert.Equal(saved.RecordedAt, retained.RecordedAt);
    Assert.True(
      (
        await f.Resolve()
          .Handle(
            new(f.Load.Id, saved.Id, new(Guid.NewGuid(), saved.Revision)),
            default
          )
      ).Success
    );
  }

  [Theory]
  [InlineData("Driver")]
  [InlineData(null)]
  public async Task NonDispatchRolesCannotReadOrWrite(string? role)
  {
    await using var f = await DispatchActivityFixture.CreateAsync();
    Assert.Equal(
      403,
      (await f.Add(role).Handle(f.Command(), default)).StatusCode
    );
    Assert.Equal(
      403,
      (await f.Read(role).Handle(new(f.Load.Id), default)).StatusCode
    );
    f.Actor.IsActive = false;
    await f.Db.SaveChangesAsync();
    Assert.Equal(403, (await f.Add().Handle(f.Command(), default)).StatusCode);
    Assert.Empty(f.Db.DispatchActivityEntries);
  }

  [Fact]
  public async Task ForeignStopAndStaleRevisionCannotAppend()
  {
    await using var f = await DispatchActivityFixture.CreateAsync();
    var other = new DispatchEntity
    {
      Id = Guid.NewGuid(),
      LoadNumber = 1002,
      Stops = [new DispatchStop { Id = Guid.NewGuid(), Sequence = 1 }],
    };
    f.Db.Dispatches.Add(other);
    await f.Db.SaveChangesAsync();
    var command = f.Command();
    Assert.Equal(
      409,
      (
        await f.Add()
          .Handle(
            command with
            {
              Update = command.Update with { StopId = other.Stops[0].Id },
            },
            default
          )
      ).StatusCode
    );
    Assert.True((await f.Add().Handle(command, default)).Success);
    Assert.Equal(409, (await f.Add().Handle(f.Command(), default)).StatusCode);
    Assert.Equal(1, await f.Db.DispatchActivityEntries.CountAsync());
  }

  [Fact]
  public async Task OldOpenIssueIsPinnedAndKeysetPagesAreBounded()
  {
    await using var f = await DispatchActivityFixture.CreateAsync();
    for (var index = 0; index < 25; index++)
      Assert.True(
        (await f.Add().Handle(f.Command(index, index == 0), default)).Success
      );
    var page = (await f.Read().Handle(new(f.Load.Id), default)).Response!;
    Assert.Equal(10, page.Items.Count);
    Assert.Equal(1, page.OpenCount);
    Assert.Equal(1, Assert.Single(page.OpenItems).CreatedRevision);
    var next = (
      await f.Read().Handle(new(f.Load.Id, page.NextBeforeRevision), default)
    ).Response!;
    Assert.Equal(10, next.Items.Count);
    Assert.Empty(
      page.Items.Select(x => x.Id).Intersect(next.Items.Select(x => x.Id))
    );
    Assert.Equal(1, Assert.Single(next.OpenItems).CreatedRevision);
  }

  [Fact]
  public async Task ImportedUpdatesAndRemovedStopsDoNotEraseJournal()
  {
    await using var f = await DispatchActivityFixture.CreateAsync();
    var added = (await f.Add().Handle(f.Command(), default)).Response!;
    DispatchMapper.Update(
      f.Load,
      new ExternalDispatch { Status = "completed" },
      null,
      null,
      null,
      null,
      DateTime.UtcNow
    );
    f.Db.DispatchStops.Remove(f.Load.Stops[0]);
    await f.Db.SaveChangesAsync();
    var page = (await f.Read().Handle(new(f.Load.Id), default)).Response!;
    var retained = Assert.Single(page.Items);
    Assert.Equal(added.StopId, retained.StopId);
    Assert.Equal(added.StopLabel, retained.StopLabel);
    Assert.Equal(added.Text, retained.Text);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task NativeBoundaryCanBeLinkedOnlyThroughThisLoad(bool sharedLeg)
  {
    await using var f = await DispatchActivityFixture.CreateAsync();
    var truck = new Truck { Id = Guid.NewGuid(), UnitNumber = "Test truck" };
    var trip = new Trip { Id = Guid.NewGuid() };
    var visit = new ExecutionTransferVisit
    {
      Id = Guid.NewGuid(),
      TripId = trip.Id,
      Operation = "Hook",
      SiteName = "Transfer yard",
    };
    var unrelated = new ExecutionTransferVisit
    {
      Id = Guid.NewGuid(),
      TripId = trip.Id,
      Operation = "Drop",
    };
    var leg = new ExecutionLeg
    {
      Id = Guid.NewGuid(),
      TripId = trip.Id,
      TruckId = truck.Id,
      Loads =
      [
        new LoadExecutionLeg
        {
          Id = Guid.NewGuid(),
          DispatchId = f.Load.Id,
          StartVisitId = visit.Id,
          EndVisitId = Guid.NewGuid(),
          Sequence = 1,
        },
      ],
    };
    f.Db.Trucks.Add(truck);
    f.Db.Trips.Add(trip);
    leg.Stops = ExecutionStopRows.Capture(
      [ExecutionSnapshots.Boundary(visit, f.Load.Id, 1, "Loaded")]
    );
    var other = new DispatchEntity { Id = Guid.NewGuid(), LoadNumber = 90001 };
    f.Db.Dispatches.Add(other);
    var otherLeg = sharedLeg
      ? leg
      : new ExecutionLeg
      {
        Id = Guid.NewGuid(),
        TripId = trip.Id,
        TruckId = truck.Id,
      };
    otherLeg.Stops.AddRange(
      ExecutionStopRows.Capture(
        [ExecutionSnapshots.Boundary(unrelated, other.Id, 1, "Loaded")]
      )
    );
    otherLeg.Loads.Add(
      new()
      {
        Id = Guid.NewGuid(),
        DispatchId = other.Id,
        StartVisitId = unrelated.Id,
        EndVisitId = unrelated.Id,
      }
    );
    if (!sharedLeg)
      f.Db.ExecutionLegs.Add(otherLeg);
    f.Db.ExecutionLegs.Add(leg);
    await f.Db.SaveChangesAsync();
    var command = f.Command();
    var result = await f.Add()
      .Handle(
        command with
        {
          Update = command.Update with { StopId = visit.Id },
        },
        default
      );
    Assert.True(result.Success);
    Assert.Equal("Hook · Transfer yard", result.Response!.StopLabel);
    Assert.Equal(
      409,
      (
        await f.Add()
          .Handle(
            f.Command(1) with
            {
              Update = f.Command(1).Update with { StopId = unrelated.Id },
            },
            default
          )
      ).StatusCode
    );
  }
}
