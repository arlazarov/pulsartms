using Application.Diagnostics;
using Application.Features.Routing.Services.FuelPlanning;
using Domain.Entities.Dispatch;
using Domain.Entities.Execution;
using Domain.Entities.Fleet;
using Server.Tests.Support;
using DispatchEntity = Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Persistence;

// The horizon reads its connections and base routes in one query each instead
// of one per load, filtered on the pairs themselves. Against the real engine
// this asks for the rows it means and nothing beside them: not a neighbouring
// leg of the same dispatch, and not the row a foreign leg would have found.
// Tests that difference PerformanceStages totals, which are per process:
// another test running beside them would move the counter inside the window
// being measured.
[CollectionDefinition(
  "Process-wide stage counters",
  DisableParallelization = true
)]
public sealed class ProcessWideStageCounterCollection;

[Collection("Process-wide stage counters")]
[Trait("Category", "Database")]
[Trait("Kind", "Integration")]
public sealed class FuelHorizonPrefetchQueryTests
{
  [RequiresPostgresFact]
  public async Task ItReadsExactlyThePairsAskedForAndNotTheirNeighbours()
  {
    await using var fixture = await PostgresFixture.CreateAsync();
    // The fixture already created this run's schema and its tables.
    var db = fixture.Connect();

    // Two trucks: only one leg per truck may be active at a time.
    var truck = new Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = "A",
      UnitNumber = "A",
    };
    var spare = new Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = "B",
      UnitNumber = "B",
    };
    db.Trucks.AddRange(truck, spare);
    // Load numbers are unique per company, so they are given distinct ones.
    var wanted = new DispatchEntity
    {
      Id = Guid.NewGuid(),
      TruckId = truck.Id,
      LoadNumber = 90001,
    };
    var other = new DispatchEntity
    {
      Id = Guid.NewGuid(),
      TruckId = truck.Id,
      LoadNumber = 90002,
    };
    db.Dispatches.AddRange(wanted, other);

    // The same dispatch carries two base routes on different legs. Asking for
    // one of them must not bring the other, whose geometry is the expensive
    // part of the payload.
    var trip = new Trip { Id = Guid.NewGuid() };
    db.Set<Trip>().Add(trip);
    var askedLeg = Leg(trip.Id, truck.Id);
    var otherLeg = Leg(trip.Id, spare.Id);
    db.ExecutionLegs.AddRange(askedLeg, otherLeg);
    db.Set<DispatchBaseRoute>()
      .AddRange(
        Base(wanted.Id, askedLeg.Id, "asked"),
        Base(wanted.Id, otherLeg.Id, "same dispatch, other leg"),
        Base(other.Id, null, "other dispatch, no leg")
      );
    db.Set<DispatchDeadhead>()
      .AddRange(
        Deadhead(wanted.Id, askedLeg.Id, "asked"),
        Deadhead(other.Id, otherLeg.Id, "not asked")
      );
    await db.SaveChangesAsync();

    var horizon = new FuelHorizon(null!, db, null!);
    var missing = new FuelHorizon.SavedKey(Guid.NewGuid(), null);
    var keys = new[]
    {
      new FuelHorizon.SavedKey(wanted.Id, askedLeg.Id),
      missing,
    };

    var before = Rows("base-rows-read");
    var bases = await horizon.ReadBaseRowsAsync(keys, default);
    // Exactly one row came back: the asked-for pair. The dictionary alone
    // could not show this - it is built from the keys either way - so the
    // count of rows the query actually returned is what is asserted. The
    // neighbour on the other leg carries its own geometry, and fetching it
    // would cost that payload and then discard it.
    Assert.Equal(1, Rows("base-rows-read") - before);
    // An entry for each key that was asked for, and nothing else.
    Assert.Equal(keys.Length, bases.Count);
    Assert.Equal("asked", bases[keys[0]]!.InputHash);
    // Covered but absent - the caller must be able to tell this from "not
    // covered", because only the second may fall back to its own query.
    Assert.True(bases.ContainsKey(missing));
    Assert.Null(bases[missing]);

    var beforeConnections = Rows("connection-rows-read");
    var connections = await horizon.ReadConnectionRowsAsync(keys, default);
    Assert.Equal(1, Rows("connection-rows-read") - beforeConnections);
    Assert.Equal(keys.Length, connections.Count);
    Assert.Equal("asked", connections[keys[0]]!.InputHash);
    Assert.Null(connections[missing]);

    // A row without a leg is reached by its dispatch, and does not drag in the
    // legged rows of the dispatch beside it.
    var loose = new[] { new FuelHorizon.SavedKey(other.Id, null) };
    var beforeLoose = Rows("base-rows-read");
    var looseBases = await horizon.ReadBaseRowsAsync(loose, default);
    Assert.Equal(1, Rows("base-rows-read") - beforeLoose);
    Assert.Single(looseBases);
    Assert.Equal("other dispatch, no leg", looseBases[loose[0]]!.InputHash);

    // A pair naming one dispatch and another dispatch's leg. The leg exists
    // and is unique, so a filter written as a list of legs would have found
    // its row and carried the payload across the wire before noticing the
    // dispatch did not match. Nothing may come back.
    var foreign = new[] { new FuelHorizon.SavedKey(other.Id, askedLeg.Id) };
    var beforeForeign = Rows("base-rows-read");
    var foreignBases = await horizon.ReadBaseRowsAsync(foreign, default);
    Assert.Equal(0, Rows("base-rows-read") - beforeForeign);
    Assert.Single(foreignBases);
    Assert.Null(foreignBases[foreign[0]]);

    var beforeForeignConnections = Rows("connection-rows-read");
    var foreignConnections = await horizon.ReadConnectionRowsAsync(
      foreign,
      default
    );
    Assert.Equal(0, Rows("connection-rows-read") - beforeForeignConnections);
    Assert.Null(foreignConnections[foreign[0]]);

    // Asking for nothing asks the database nothing.
    Assert.Empty(await horizon.ReadBaseRowsAsync([], default));
    Assert.Empty(await horizon.ReadConnectionRowsAsync([], default));
  }

  // Rows the query returned, cumulative from process start, so tests read it
  // as a difference.
  private static long Rows(string stage) =>
    PerformanceStages
      .Snapshot()
      .GetValueOrDefault($"fuel-horizon/{stage}")
      ?.Items ?? 0;

  private static ExecutionLeg Leg(Guid trip, Guid truck) =>
    new()
    {
      Id = Guid.NewGuid(),
      TripId = trip,
      TruckId = truck,
      Status = "active",
      Revision = 1,
    };

  private static DispatchBaseRoute Base(
    Guid dispatch,
    Guid? leg,
    string hash
  ) =>
    new()
    {
      Id = Guid.NewGuid(),
      DispatchId = dispatch,
      ExecutionLegId = leg,
      InputHash = hash,
      RouteJson = "{}",
      CalculatedAt = DateTime.UtcNow,
    };

  private static DispatchDeadhead Deadhead(
    Guid dispatch,
    Guid? leg,
    string hash
  ) =>
    new()
    {
      Id = Guid.NewGuid(),
      DispatchId = dispatch,
      ExecutionLegId = leg,
      InputHash = hash,
      PreviousDispatchId = Guid.NewGuid(),
      CalculatedAt = DateTime.UtcNow,
    };
}
