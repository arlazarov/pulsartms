using System.Collections.Immutable;
using Application.Features.Execution.Models;
using Domain.Entities.Execution;
using Domain.Models.Routing;
using Domain.Rules;
using Microsoft.EntityFrameworkCore;
using Server.Tests.Support;

namespace Server.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Integration")]
public sealed class HistoricalWorkPublicationTests
{
  [Fact]
  public async Task WorkAndHistoryValidateInsideTheSameOwnedTransaction()
  {
    var probe = new HistoricalReadProbe();
    await using var f = await SavedFuelHorizonFixture.CreateAsync(
      historyReads: probe
    );
    await HistoricalWorkFixture.AddAsync(f.Db, f.Current);
    var work = await f.Services.FuelInputs.ReadFreshAsync(
      f.State.Plan!.TruckId,
      default
    );
    var horizon = await f.Horizon.BuildAsync(
      f.State,
      f.State.Profile,
      default,
      work
    );
    probe.Enabled = true;

    await using (
      var transaction = await f.Services.Publication.BeginAsync(
        work.Itinerary,
        horizon.History,
        default
      )
    )
    {
      Assert.Same(transaction, f.Db.Database.CurrentTransaction);
      Assert.NotNull(probe.Transactions[0]);
      Assert.All(
        probe.Transactions,
        current => Assert.Same(probe.Transactions[0], current)
      );
      Assert.Contains(probe.Commands, sql => sql.Contains("ExecutionLegs"));
      await transaction.CommitAsync();
    }

    Assert.Null(f.Db.Database.CurrentTransaction);
    Assert.Equal(1, f.Publication.Calls);
    Assert.Equal(0, f.Router.Calls);
  }

  [Fact]
  public async Task NativeHistoryRevalidationRetainsEachOriginalBatch()
  {
    await using var f = await SavedFuelHorizonFixture.CreateAsync();
    var first = await HistoricalWorkFixture.AddAsync(f.Db, f.Current);
    var older = await HistoricalWorkFixture.AddAsync(f.Db, first);
    var trip = new Trip { Id = Guid.NewGuid() };
    var leg = new ExecutionLeg
    {
      Id = Guid.NewGuid(),
      TripId = trip.Id,
      TruckId = f.State.Plan!.TruckId,
      Status = "completed",
      Revision = 1,
      Stops = ExecutionStopRows.Capture(older.Stops),
    };
    leg.Loads.Add(
      new()
      {
        Id = Guid.NewGuid(),
        DispatchId = older.Id,
        Sequence = 1,
      }
    );
    f.Db.AddRange(trip, leg);
    await f.Db.SaveChangesAsync();
    var work = await f.Services.FuelInputs.ReadFreshAsync(
      f.State.Plan.TruckId,
      default
    );
    var grouped = await f.Services.DeadheadHistory.ReadLoadedAsync(
      [f.Current, f.Future],
      default
    );
    var separate = await f.Services.DeadheadHistory.ReadLoadedAsync(
      [f.Future],
      default
    );
    Assert.Contains(
      grouped[f.Future.Id].Predecessors,
      x => x.ExecutionLegId == leg.Id
    );
    Assert.DoesNotContain(
      separate[f.Future.Id].Predecessors,
      x => x.ExecutionLegId == leg.Id
    );
    DeadheadHistoryBatch[] batches =
    [
      new(grouped.Values.ToImmutableArray()),
      new(separate.Values.ToImmutableArray()),
    ];

    await using (
      var transaction = await f.Services.Publication.BeginAsync(
        work.Itinerary,
        batches,
        default
      )
    )
      await transaction.CommitAsync();
    leg.Revision++;
    await f.Db.SaveChangesAsync();

    await Assert.ThrowsAsync<RoutePlanningException>(
      () => f.Services.Publication.BeginAsync(work.Itinerary, batches, default)
    );
    Assert.Null(f.Db.Database.CurrentTransaction);
  }
}
