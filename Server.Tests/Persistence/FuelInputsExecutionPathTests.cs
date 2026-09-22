using System.Data;
using Application.Diagnostics;
using Application.Features.Execution.Models;
using Domain.Entities.Dispatch;
using Domain.Entities.Execution;
using Domain.Entities.Fleet;
using Infrastructure.Persistence;
using Server.Tests.Support;
using DispatchEntity = Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Persistence;

// What reading the fuel work inputs costs on the execution-backed path, which
// is the one production takes and the one the SQLite legacy fixture does not
// exercise. Counted against the real engine, with transaction boundaries apart
// from statements: the snapshot read pays for its BEGIN and COMMIT too.
[Collection("Process-wide stage counters")]
[Trait("Category", "Fuel")]
[Trait("Kind", "Integration")]
public sealed class FuelInputsExecutionPathTests
{
  // What this path costs today, beside the BEGIN and COMMIT.
  private const int StatementsPerExecutionRead = 6;

  [RequiresPostgresFact]
  public async Task AFreshReadIsOneSnapshotAndDoesNotAskForLegacyWork()
  {
    var probe = new QueryColumnProbe();
    await using var fixture = await PostgresFixture.CreateAsync();
    var db = fixture.Connect(probe);
    var truck = await SeedChainAsync(db, legs: 3);
    var services = new PlanningTestServices(db);

    // The fixture has to be the path it claims to be: work carrying execution
    // legs, not source rows sitting beside them.
    var first = await services.FuelInputs.ReadFreshAsync(truck, default);
    Assert.NotEmpty(first.Itinerary.Segments);
    Assert.All(
      first.Itinerary.Segments,
      x => Assert.NotNull(x.Work.ExecutionLegId)
    );
    // Warm: the first read also pays for EF model building and the connection.
    await services.FuelInputs.ReadFreshAsync(truck, default);

    var counts = new List<int>();
    for (var run = 0; run < 5; run++)
    {
      probe.Clear();
      await services.FuelInputs.ReadFreshAsync(truck, default);
      counts.Add(probe.Statements.Count);
      Assert.Equal(1, probe.TransactionsStarted);
      Assert.Equal(1, probe.TransactionsCommitted);
      Assert.All(
        probe.Isolation,
        x => Assert.Equal(IsolationLevel.RepeatableRead, x)
      );
      Assert.DoesNotContain(
        probe.Statements.GroupBy(x => x),
        x => x.Count() > 1
      );
      // Work carrying an execution leg is read natively, so the wide legacy
      // load - a dispatch with its stops and its source link - has nothing to
      // ask for and must not be asked.
      Assert.DoesNotContain(probe.Statements, IsLegacyDispatchLoad);
    }

    // Warmed repetitions cost the same: nothing here grows with repetition.
    Assert.Single(counts.Distinct());
    Assert.Equal(StatementsPerExecutionRead, counts[0]);
  }

  [RequiresPostgresFact]
  public async Task TheReadIsDividedIntoStagesThatAccountForIt()
  {
    await using var fixture = await PostgresFixture.CreateAsync();
    var db = fixture.Connect();
    var truck = await SeedChainAsync(db, legs: 2);
    var services = new PlanningTestServices(db);

    await services.FuelInputs.ReadFreshAsync(truck, default);
    var before = Stages();
    await services.FuelInputs.ReadFreshAsync(truck, default);
    var after = Stages();

    long Moved(string stage) =>
      after.GetValueOrDefault(stage) - before.GetValueOrDefault(stage);

    // One snapshot opened and committed, one pass through the reader.
    Assert.Equal(1, Moved("execution-scope/open"));
    Assert.Equal(1, Moved("execution-scope/commit"));
    Assert.Equal(1, Moved("itinerary-read/read-total"));
    // Every part of the reader is named, so the remainder can be computed.
    foreach (var stage in Parts)
      Assert.Equal(1, Moved($"itinerary-read/{stage}"));
  }

  private static readonly string[] Parts =
  [
    "work-batch",
    "legacy",
    "evidence",
    "assemble",
  ];

  private static Dictionary<string, long> Stages() =>
    PerformanceStages.Snapshot().ToDictionary(x => x.Key, x => x.Value.Count);

  [RequiresPostgresFact]
  public async Task AMixedChainStillReadsItsLegacyWork()
  {
    var probe = new QueryColumnProbe();
    await using var fixture = await PostgresFixture.CreateAsync();
    var db = fixture.Connect(probe);
    var truck = await SeedChainAsync(db, legs: 2, withoutLeg: 1);
    var services = new PlanningTestServices(db);

    await services.FuelInputs.ReadFreshAsync(truck, default);
    probe.Clear();
    var snapshot = await services.FuelInputs.ReadFreshAsync(truck, default);

    // Both kinds of work survive, and the legacy read still happens for the
    // half that needs it.
    var segments = snapshot.Itinerary.Segments;
    Assert.Contains(segments, x => x.Work.ExecutionLegId.HasValue);
    Assert.Contains(segments, x => !x.Work.ExecutionLegId.HasValue);
    Assert.Contains(probe.Statements, IsLegacyDispatchLoad);
    Assert.Equal(1, probe.TransactionsStarted);
    Assert.Equal(1, probe.TransactionsCommitted);
  }

  // The wide read of a dispatch with its stops: the only statement here that
  // joins the stops and the source link.
  private static bool IsLegacyDispatchLoad(string statement) =>
    statement.Contains("\"DispatchStops\"")
    && statement.Contains("\"DispatchSourceLinks\"")
    && statement.Contains("\"CarrierName\"");

  private static async Task<Guid> SeedChainAsync(
    AppDbContext db,
    int legs,
    int withoutLeg = 0
  )
  {
    var truck = new Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = "inputs-path",
      UnitNumber = "IP",
      IsActive = true,
    };
    db.Trucks.Add(truck);
    var trip = new Trip { Id = Guid.NewGuid() };
    db.Set<Trip>().Add(trip);
    var day = DateOnly.FromDateTime(DateTime.UtcNow);
    for (var i = 0; i < legs + withoutLeg; i++)
    {
      var load = new DispatchEntity
      {
        Id = Guid.NewGuid(),
        TruckId = truck.Id,
        LoadNumber = 70000 + i,
        Status = "assigned",
        ShipDate = day,
        DeliveryDate = day,
        Stops = Enumerable
          .Range(0, 2)
          .Select(stop => new DispatchStop
          {
            Id = Guid.NewGuid(),
            Sequence = stop + 1,
            Job = stop == 0 ? "Pick Up" : "Drop Off",
            Name = $"Stop {i}-{stop}",
            Latitude = 40 + i + stop * 0.1m,
            Longitude = -100 + i + stop * 0.1m,
            ScheduledDate = day,
          })
          .ToList(),
      };
      db.Dispatches.Add(load);
      if (i >= legs)
        continue;
      // Only one leg per truck may be active, so the followers are planned;
      // the reader includes planned work.
      var leg = new ExecutionLeg
      {
        Id = Guid.NewGuid(),
        TripId = trip.Id,
        TruckId = truck.Id,
        Status = i == 0 ? "active" : "planned",
        Revision = 1,
        RecordedAt = DateTime.UtcNow,
        Stops = ExecutionStopRows.Capture(load.Stops),
      };
      db.ExecutionLegs.Add(leg);
      db.LoadExecutionLegs.Add(
        new LoadExecutionLeg
        {
          Id = Guid.NewGuid(),
          DispatchId = load.Id,
          ExecutionLeg = leg,
          Sequence = 1,
          StartVisitId = load.Stops[0].Id,
          EndVisitId = load.Stops[^1].Id,
        }
      );
    }
    await db.SaveChangesAsync();
    return truck.Id;
  }
}
