using Application.Features.Routing.Services.Deadheads;
using Application.Features.Routing.Services.Routes;
using Microsoft.EntityFrameworkCore;
using Server.Tests.Support;

namespace Server.Tests.Routing;

[Trait("Category", "Finance")]
[Trait("Kind", "Integration")]
public sealed class DeadheadHistorySnapshotTests
{
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
