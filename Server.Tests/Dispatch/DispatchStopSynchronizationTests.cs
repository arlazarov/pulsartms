using Application.Features.Dispatch.Commands.SyncDispatche;
using Application.Features.Dispatch.Interfaces;
using Application.Features.Dispatch.Models;
using Domain.Entities.Fleet;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Server.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Integration")]
public sealed class DispatchStopSynchronizationTests
{
  [Fact]
  public async Task ExistingLoadAcceptsAdditionalVisitsAndInvalidatesRoutesOnlyAfterChangedSync()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    var truck = new Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = "multi-stop",
      UnitNumber = "11007",
      IsActive = true,
    };
    db.Trucks.Add(truck);
    await db.SaveChangesAsync();
    var source = Load(
      1383,
      [Visit(1, "Webster", 2), Visit(2, "Port Saint Lucie", 5, "Drop Off")]
    );
    var successor = Load(1384, [Visit(1, "Next pickup", 8)]);
    using var reads = TestCache.Create();
    using var memory = new MemoryCache(new MemoryCacheOptions());
    var preparation = TestCache.Preparation();
    var handler = new SyncDispatchesCommandHandler(
      db,
      [new Provider([source, successor])],
      DispatchImportTestData.Options,
      reads,
      memory,
      preparation
    );
    await handler.Handle(new(), default);
    var saved = await db
      .Dispatches.AsNoTracking()
      .Include(x => x.Stops)
      .SingleAsync(x => x.LoadNumber == 1383);
    var originalIds = saved.Stops.ToDictionary(x => x.Sequence, x => x.Id);
    var dispatchIds = await db.Dispatches.Select(x => x.Id).ToArrayAsync();
    foreach (var work in preparation.Take(10))
      preparation.Complete(work, "prepared", truck.Id);
    var generation = reads.Generation("dispatch");
    db.ChangeTracker.Clear();

    source.Stops =
    [
      Visit(1, "Webster", 2),
      Visit(2, "Amsterdam", 11),
      Visit(3, "Webster", 13),
      Visit(4, "Webster", 14),
      Visit(5, "Port Saint Lucie", 5, "Drop Off"),
    ];
    var result = await handler.Handle(new(), default);

    Assert.True(result.Response > 0);
    var stops = await db
      .DispatchStops.AsNoTracking()
      .Where(x => x.DispatchId == saved.Id)
      .OrderBy(x => x.Sequence)
      .ToArrayAsync();
    Assert.Equal([1, 2, 3, 4, 5], stops.Select(x => x.Sequence));
    Assert.Equal(5, stops.Select(x => x.Id).Distinct().Count());
    Assert.Equal(originalIds[1], stops[0].Id);
    Assert.Equal(originalIds[2], stops[4].Id);
    Assert.DoesNotContain(
      stops.Skip(1).Take(3),
      x => originalIds.Values.Contains(x.Id)
    );
    Assert.Equal(
      [new TimeOnly(2, 0), new TimeOnly(13, 0), new TimeOnly(14, 0)],
      stops.Where(x => x.City == "Webster").Select(x => x.ScheduledTime)
    );
    Assert.All(stops, x => Assert.Equal(truck.Id, x.TruckId));
    Assert.True(reads.Generation("dispatch") > generation);
    var pending = preparation.Take(10);
    Assert.Equal(
      dispatchIds.Order(),
      pending.Select(x => x.DispatchId).Order()
    );
    foreach (var work in pending)
      preparation.Complete(work, "prepared", truck.Id);
    generation = reads.Generation("dispatch");
    db.ChangeTracker.Clear();
    memory.Remove("dispatch-sync-signature:fixture");

    var replay = await handler.Handle(new(), default);

    Assert.Equal(0, replay.Response);
    Assert.Equal(generation, reads.Generation("dispatch"));
    Assert.Equal(0, preparation.PendingCount);
    Assert.Equal(
      stops.Select(x => x.Id),
      await db
        .DispatchStops.AsNoTracking()
        .Where(x => x.DispatchId == saved.Id)
        .OrderBy(x => x.Sequence)
        .Select(x => x.Id)
        .ToArrayAsync()
    );
  }

  [Fact]
  public async Task RemovedSequencesDoNotDeleteOtherVisitsToTheSameFacility()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    var source = Load(
      1383,
      [
        Visit(1, "Webster", 2),
        Visit(2, "Amsterdam", 11),
        Visit(3, "Webster", 13),
        Visit(4, "Webster", 14),
        Visit(5, "Port Saint Lucie", 5, "Drop Off"),
      ]
    );
    using var reads = TestCache.Create();
    using var memory = new MemoryCache(new MemoryCacheOptions());
    var preparation = TestCache.Preparation();
    var handler = new SyncDispatchesCommandHandler(
      db,
      [new Provider([source])],
      DispatchImportTestData.Options,
      reads,
      memory,
      preparation
    );
    await handler.Handle(new(), default);
    var originalIds = await db
      .DispatchStops.AsNoTracking()
      .ToDictionaryAsync(x => x.Sequence, x => x.Id);
    foreach (var work in preparation.Take(10))
      preparation.Complete(work, "prepared", null);
    db.ChangeTracker.Clear();

    source.Stops = source.Stops.Where(x => x.Sequence is 1 or 3 or 5).ToList();
    await handler.Handle(new(), default);

    var stops = await db
      .DispatchStops.AsNoTracking()
      .OrderBy(x => x.Sequence)
      .ToArrayAsync();
    Assert.Equal([1, 3, 5], stops.Select(x => x.Sequence));
    Assert.Equal(2, stops.Count(x => x.City == "Webster"));
    Assert.All(stops, x => Assert.Equal(originalIds[x.Sequence], x.Id));
    Assert.Equal(1, preparation.PendingCount);
  }

  [Fact]
  public async Task RemovingLeadingStopPreservesPickupAnchorAndManualFactsOnTheSameVisit()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var truck = new Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = "renumbered",
      UnitNumber = "11007",
      IsActive = true,
    };
    f.Db.Trucks.Add(truck);
    var source = Load(
      1376,
      [
        Visit(1, "Driver origin", 1, "Driver start"),
        Visit(2, "Webster", 2),
        Visit(3, "Port Saint Lucie", 5, "Drop Off"),
      ]
    );
    source.TruckNumber = "";
    source.Stops.ForEach(s => s.TruckNumber = "");
    f.Sources.Add(source);
    await f.Handler.Handle(new(), default);
    var load = await f.Db.Dispatches.Include(x => x.Stops).SingleAsync();
    var pickup = load.Stops.Single(s => s.Sequence == 2);
    var delivery = load.Stops.Single(s => s.Sequence == 3);
    load.PlanningTruckId = truck.Id;
    load.PlanningFromStopId = pickup.Id;
    pickup.ManualAction = "Pick Up";
    pickup.ManualStateAfter = "Loaded";
    pickup.OperationRevision = 1;
    pickup.ManualCompletedAt = DateTime.UtcNow.AddHours(-1);
    pickup.ManualCompletionRevision = 1;
    pickup.Address = "Verified street address";
    pickup.AddressVerifiedAt = DateTime.UtcNow;
    pickup.Latitude = 43;
    pickup.Longitude = -77;
    await f.Db.SaveChangesAsync();
    f.Db.ChangeTracker.Clear();

    source.Stops =
    [
      Visit(1, "Webster", 2),
      Visit(2, "Port Saint Lucie", 5, "Drop Off"),
    ];
    source.Stops.ForEach(s => s.TruckNumber = "");
    await f.Handler.Handle(new(), default);

    var saved = await f
      .Db.Dispatches.AsNoTracking()
      .Include(x => x.Stops)
      .SingleAsync();
    var stops = saved.Stops.OrderBy(s => s.Sequence).ToArray();
    Assert.Equal([pickup.Id, delivery.Id], stops.Select(s => s.Id));
    Assert.Equal(pickup.Id, saved.PlanningFromStopId);
    Assert.Equal(2, saved.TruckItinerary().Stops.Count);
    Assert.Equal("Pick Up", stops[0].ManualAction);
    Assert.Equal("Loaded", stops[0].ManualStateAfter);
    Assert.Equal(1, stops[0].OperationRevision);
    Assert.Equal(pickup.ManualCompletedAt, stops[0].ManualCompletedAt);
    Assert.Equal(1, stops[0].ManualCompletionRevision);
    Assert.Equal("Verified street address", stops[0].Address);
    Assert.Equal(pickup.AddressVerifiedAt, stops[0].AddressVerifiedAt);
    Assert.Null(stops[1].ManualCompletedAt);
    Assert.Null(stops[1].ManualAction);
  }

  [Fact]
  public async Task RepeatedFacilityVisitsKeepAppointmentIdentityWhenEarlierVisitIsRemovedAndRestRenumbered()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var source = Load(
      1383,
      [Visit(1, "Webster", 2), Visit(2, "Webster", 13), Visit(3, "Webster", 14)]
    );
    f.Sources.Add(source);
    await f.Handler.Handle(new(), default);
    var ids = await f
      .Db.DispatchStops.AsNoTracking()
      .ToDictionaryAsync(s => s.ScheduledTime!.Value, s => s.Id);
    f.Db.ChangeTracker.Clear();

    source.Stops =
    [
      Visit(1, "Webster", 13),
      Visit(2, "Webster", 14),
      Visit(3, "Webster", 15),
    ];
    await f.Handler.Handle(new(), default);

    var stops = await f
      .Db.DispatchStops.AsNoTracking()
      .OrderBy(s => s.Sequence)
      .ToArrayAsync();
    Assert.Equal(ids[new(13, 0)], stops[0].Id);
    Assert.Equal(ids[new(14, 0)], stops[1].Id);
    Assert.DoesNotContain(stops[2].Id, ids.Values);
  }

  [Theory]
  [InlineData("Webster", "Drop Off")]
  [InlineData("Different city", "Pick Up")]
  public async Task ReplacementAtSameSequenceCannotInheritRemovedStartingStop(
    string city,
    string job
  )
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var source = Load(1383, [Visit(1, "Webster", 2)]);
    f.Sources.Add(source);
    await f.Handler.Handle(new(), default);
    var load = await f.Db.Dispatches.Include(x => x.Stops).SingleAsync();
    var oldId = load.Stops[0].Id;
    load.PlanningFromStopId = oldId;
    load.Stops[0].ManualCompletedAt = DateTime.UtcNow;
    load.Stops[0].ManualCompletionRevision = 1;
    await f.Db.SaveChangesAsync();
    f.Db.ChangeTracker.Clear();
    source.Stops = [Visit(1, city, 2, job)];

    await f.Handler.Handle(new(), default);

    var saved = await f
      .Db.Dispatches.AsNoTracking()
      .Include(x => x.Stops)
      .SingleAsync();
    var stop = Assert.Single(saved.Stops);
    Assert.NotEqual(oldId, stop.Id);
    Assert.Null(stop.ManualCompletedAt);
    Assert.Equal(oldId, saved.PlanningFromStopId);
    Assert.Empty(saved.TruckItinerary().Stops);
  }

  private static ExternalDispatch Load(
    int number,
    List<ExternalDispatchStop> stops
  ) =>
    new()
    {
      LoadNumber = number,
      TruckNumber = "11007",
      Status = "assigned",
      Stops = stops,
    };

  private static ExternalDispatchStop Visit(
    int sequence,
    string city,
    int hour,
    string job = "Pick Up"
  ) =>
    new()
    {
      Sequence = sequence,
      Job = job,
      Name = city + " facility",
      Address = "1 " + city + " Road",
      City = city,
      Country = "US",
      TruckNumber = "11007",
      ScheduledDate = new DateOnly(2026, 9, 11),
      ScheduledTime = new TimeOnly(hour, 0),
    };

  private sealed class Provider(IReadOnlyList<ExternalDispatch> sources)
    : IDispatchProvider
  {
    public string Key => DispatchImportTestData.Key;
    public string DisplayName => DispatchImportTestData.DisplayName;

    public Task<IReadOnlyList<ExternalDispatch>> GetDispatchesAsync(
      CancellationToken ct = default
    ) => Task.FromResult(DispatchImportTestData.Identify(sources));

    public Task<IReadOnlyList<ExternalDispatch>> GetDispatchesAsync(
      DateOnly from,
      DateOnly to,
      CancellationToken ct = default
    ) => Task.FromResult(DispatchImportTestData.Identify(sources));
  }
}
