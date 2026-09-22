using System.Collections.Immutable;
using Application.Features.Routing.Services.Deadheads;
using Application.Features.Routing.Services.Routes;
using Domain.Rules.Routing;
using Microsoft.EntityFrameworkCore;
using Server.Tests.Support;

namespace Server.Tests.Routing;

[Trait("Category", "Finance")]
[Trait("Kind", "Integration")]
public sealed class DeadheadHistorySnapshotTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task BatchedNativeHistoryDoesNotWidenCompletedEligibility(
    bool savedReference
  )
  {
    await using var f = await SavedFuelHorizonFixture.CreateAsync();
    var leg = await f.ReceiveCurrentAsync();
    leg.Status = "completed";
    if (savedReference)
    {
      var saved = await f.Db.DispatchDeadheads.SingleAsync(x =>
        x.DispatchId == f.Future.Id
      );
      saved.PreviousExecutionLegId = leg.Id;
    }
    await f.Db.SaveChangesAsync();
    var first = RouteWorkProjection.Capture(f.Future);
    var earlier = first.ShipDate!.Value.AddDays(-10);
    var second = first with
    {
      Id = Guid.NewGuid(),
      ShipDate = earlier,
      Stops = first
        .Stops.Select(x => x with { ScheduledDate = earlier })
        .ToImmutableArray(),
    };
    if (savedReference)
      first = first with { ShipDate = earlier, Stops = second.Stops };
    var expectedFirst = await f.Services.DeadheadHistory.ReadLoadedAsync(
      [first],
      default
    );
    var expectedSecond = await f.Services.DeadheadHistory.ReadLoadedAsync(
      [second],
      default
    );
    var actual = await f.Services.DeadheadHistory.ReadLoadedBatchesAsync(
      [
        [first],
        [second],
      ],
      default
    );
    Assert.Equal(
      expectedFirst[first.Id].InputSignature,
      actual[0][first.Id].InputSignature
    );
    Assert.Equal(
      expectedSecond[second.Id].InputSignature,
      actual[1][second.Id].InputSignature
    );
    Assert.Contains(
      actual[0][first.Id].Predecessors,
      x => x.ExecutionLegId == leg.Id
    );
    Assert.DoesNotContain(
      actual[1][second.Id].Predecessors,
      x => x.ExecutionLegId == leg.Id
    );
  }

  [Fact]
  public async Task RepeatedDispatchBatchesPreserveSeparateCapturedInputs()
  {
    await using var f = await SavedFuelHorizonFixture.CreateAsync();
    var first = RouteWorkProjection.Capture(f.Future);
    var second = first with
    {
      RouteChoiceRevision = first.RouteChoiceRevision + 1,
    };
    var expectedFirst = await f.Services.DeadheadHistory.ReadLoadedAsync(
      [first],
      default
    );
    var expectedSecond = await f.Services.DeadheadHistory.ReadLoadedAsync(
      [second],
      default
    );
    var actual = await f.Services.DeadheadHistory.ReadLoadedBatchesAsync(
      [
        [first],
        [],
        [second],
      ],
      default
    );
    Assert.Equal(
      expectedFirst[first.Id].InputSignature,
      actual[0][first.Id].InputSignature
    );
    Assert.Empty(actual[1]);
    Assert.Equal(
      expectedSecond[second.Id].InputSignature,
      actual[2][second.Id].InputSignature
    );
    Assert.Equal(
      second.RouteChoiceRevision,
      actual[2][second.Id].Current.RouteChoiceRevision
    );
  }

  [Fact]
  public async Task CompletedHistoryHydratesInTheCandidateReadTransaction()
  {
    var probe = new HistoricalReadProbe();
    await using var f = await SavedFuelHorizonFixture.CreateAsync(
      historyReads: probe
    );
    var leg = await f.ReceiveCurrentAsync();
    leg.Status = "completed";
    await f.Db.SaveChangesAsync();
    probe.Enabled = true;

    var snapshot = (
      await f.Services.DeadheadHistory.ReadAsync([f.Future.Id], default)
    )[f.Future.Id];

    probe.Enabled = false;
    var connection = DeadheadConnection.Find(snapshot)!;
    Assert.Equal(leg.Id, connection.Previous.ExecutionLegId);
    Assert.Equal("completed", connection.Previous.ExecutionStatus);
    Assert.True(probe.Commands.Count > 3);
    Assert.Contains(probe.Commands, sql => sql.Contains("ExecutionLegs"));
    Assert.Contains(probe.Commands, sql => sql.Contains("SwitchParticipants"));
    Assert.NotNull(probe.Transactions[0]);
    Assert.All(
      probe.Transactions,
      transaction => Assert.Same(probe.Transactions[0], transaction)
    );
    Assert.Null(f.Db.Database.CurrentTransaction);
    Assert.True(
      await f.Services.DeadheadHistory.MatchesAsync(snapshot, default)
    );

    leg.Revision++;
    await f.Db.SaveChangesAsync();

    Assert.False(
      await f.Services.DeadheadHistory.MatchesAsync(snapshot, default)
    );
    Assert.Equal(7, connection.Previous.AssignmentRevision);
  }

  [Fact]
  public async Task SuppliedCurrentFactsAreCapturedBeforeAsynchronousLookup()
  {
    var probe = new HistoricalReadProbe();
    await using var f = await SavedFuelHorizonFixture.CreateAsync(
      historyReads: probe
    );
    var supplied = HistoricalWorkFixture.Copy(f.Future);
    await f
      .Db.Dispatches.Where(x => x.Id == f.Future.Id)
      .ExecuteUpdateAsync(s => s.SetProperty(x => x.TruckId, (Guid?)null));
    await f
      .Db.DispatchStops.Where(x => x.DispatchId == f.Future.Id)
      .ExecuteUpdateAsync(s => s.SetProperty(x => x.TruckId, (Guid?)null));
    probe.BeforeFirstRead = () =>
    {
      supplied.TruckId = null;
      supplied.Stops.Clear();
    };
    probe.Enabled = true;

    var snapshot = (
      await f.Services.DeadheadHistory.ReadLoadedAsync([supplied], default)
    )[supplied.Id];

    probe.Enabled = false;
    Assert.Equal(f.Future.TruckId, snapshot.Current.TruckId);
    Assert.Equal(2, snapshot.Current.Stops.Length);
    Assert.Equal(f.Current.Id, DeadheadConnection.Find(snapshot)!.Previous.Id);
    Assert.Null(supplied.TruckId);
    Assert.Empty(supplied.Stops);
    Assert.NotNull(probe.Transactions[0]);
    Assert.All(
      probe.Transactions,
      transaction => Assert.Same(probe.Transactions[0], transaction)
    );
  }
}
