using Application.Diagnostics;
using Application.Features.Dispatch.Models;
using Application.Features.Execution.Commands;
using Application.Features.Execution.Models;
using Application.Features.Execution.Services;
using Application.Interfaces;
using Application.Reference;
using Domain.Entities;
using Domain.Entities.Mileage;
using Domain.Rules;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Dispatch;

// AMF1399 on truck 11005: accepted, started, then cancelled at the source
// while the truck took another load. The started work leaves the truck's
// current and future work but keeps its stops, movements and history, and
// waits for a dispatcher to close it; the new load is the truck's work.
[Trait("Category", "Dispatch")]
[Trait("Kind", "Integration")]
public sealed class HeldExecutionTests
{
  [Fact]
  public async Task StartedWorkOfACancelledLoadIsHeldAndTheNewLoadIsTheTrucksWork()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var truck = await TruckAsync(f);
    var cancelled = Source("1399", "in_transit");
    cancelled.Stops[0].PickedUpAt = DateTime.UtcNow.AddHours(-30);
    f.Sources.Add(cancelled);
    await SyncAsync(f);
    var held = await LegAsync(f, "1399");
    Assert.Equal("active", held.Status);
    await MovementAsync(f, truck, held.Id);
    var before = await ItineraryAsync(f, truck);

    cancelled.Status = "cancelled";
    var replacement = Source("1407", "in_transit");
    replacement.Stops[0].PickedUpAt = DateTime.UtcNow.AddHours(-2);
    f.Sources.Add(replacement);
    await SyncAsync(f);

    held = await LegAsync(f, "1399");
    Assert.Equal(SourceCancellation.Held, held.Status);
    Assert.Equal(SourceCancellation.HeldReason, held.SourceReviewReason);
    Assert.NotNull(ExecutionStopRows.Read(held)[0].PickedUpAt);
    Assert.Single(await f.Db.Movements.AsNoTracking().ToListAsync());
    var history = await f
      .Db.ExecutionLegRevisions.AsNoTracking()
      .Where(x => x.ExecutionLegId == held.Id)
      .OrderBy(x => x.Revision)
      .Select(x => x.Operation)
      .ToListAsync();
    Assert.Equal(
      ["source-assignment-accepted", "source-synchronized"],
      history
    );
    Assert.Equal("active", (await LegAsync(f, "1407")).Status);

    // Only the new load is the truck's work, and the work's signature
    // moved: a calculation begun for the old work cannot be published.
    var after = await ItineraryAsync(f, truck);
    Assert.Equal(
      [(await LoadAsync(f, "1407")).Id],
      after.Segments.Select(x => x.Work.DispatchId)
    );
    Assert.NotEqual(before.InputSignature, after.InputSignature);

    // Synchronizing the same cancelled source again changes nothing.
    await SyncAsync(f);
    Assert.Equal(
      2,
      await f.Db.ExecutionLegRevisions.CountAsync(x =>
        x.ExecutionLegId == held.Id
      )
    );
    Assert.Equal(SourceCancellation.Held, (await LegAsync(f, "1399")).Status);
  }

  [Fact]
  public async Task WorkChangedHereIsHeldEvenIfItNeverStarted()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    await TruckAsync(f);
    var source = Source("1399", "assigned");
    f.Sources.Add(source);
    await SyncAsync(f);
    var leg = await LegAsync(f, "1399");
    await f
      .Db.ExecutionLegRevisions.Where(x => x.ExecutionLegId == leg.Id)
      .ExecuteUpdateAsync(x =>
        x.SetProperty(r => r.RecordedBy, Guid.NewGuid())
      );

    source.Status = "cancelled";
    await SyncAsync(f);

    Assert.Equal(SourceCancellation.Held, (await LegAsync(f, "1399")).Status);
  }

  [Fact]
  public async Task ADispatcherClosesHeldWorkOnceAndKeepsItsHistory()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var truck = await TruckAsync(f);
    var source = Source("1399", "in_transit");
    source.Stops[0].PickedUpAt = DateTime.UtcNow.AddHours(-30);
    f.Sources.Add(source);
    await SyncAsync(f);
    await MovementAsync(f, truck, (await LegAsync(f, "1399")).Id);
    source.Status = "cancelled";
    await SyncAsync(f);
    var leg = await LegAsync(f, "1399");
    var load = await LoadAsync(f, "1399");
    var actor = await ActorAsync(f);
    var key = Guid.NewGuid();

    var stale = await CloseAsync(f, actor, load.Id, new(leg.Id, 1, key));
    Assert.Equal(409, stale.StatusCode);

    var closed = await CloseAsync(
      f,
      actor,
      load.Id,
      new(leg.Id, leg.Revision, key)
    );
    Assert.True(closed.Success, string.Join(";", closed.Errors ?? []));
    var done = await LegAsync(f, "1399");
    Assert.Equal("cancelled", done.Status);
    Assert.Null(done.SourceReviewReason);
    Assert.NotNull(ExecutionStopRows.Read(done)[0].PickedUpAt);
    Assert.Single(await f.Db.Movements.AsNoTracking().ToListAsync());
    var last = await f
      .Db.ExecutionLegRevisions.AsNoTracking()
      .Where(x => x.ExecutionLegId == leg.Id)
      .OrderByDescending(x => x.Revision)
      .FirstAsync();
    Assert.Equal(
      ("source-cancellation-closed", (Guid?)actor.Id),
      (last.Operation, last.RecordedBy)
    );

    // The same request again is the same answer, not a second change.
    var again = await CloseAsync(
      f,
      actor,
      load.Id,
      new(leg.Id, leg.Revision, key)
    );
    Assert.True(again.Success);
    Assert.Equal(
      closed.Response!.AssignmentRevision,
      again.Response!.AssignmentRevision
    );
    Assert.Equal(
      3,
      await f.Db.ExecutionLegRevisions.CountAsync(x =>
        x.ExecutionLegId == leg.Id
      )
    );

    // Closed work is not held any more.
    var twice = await CloseAsync(
      f,
      actor,
      load.Id,
      new(leg.Id, done.Revision, Guid.NewGuid())
    );
    Assert.Equal(409, twice.StatusCode);
  }

  [Fact]
  public async Task WorkIsNotClosedWhenTheSourceRestoredTheLoad()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    await TruckAsync(f);
    var source = Source("1399", "in_transit");
    source.Stops[0].PickedUpAt = DateTime.UtcNow.AddHours(-30);
    f.Sources.Add(source);
    await SyncAsync(f);
    source.Status = "cancelled";
    await SyncAsync(f);
    var leg = await LegAsync(f, "1399");
    var load = await LoadAsync(f, "1399");
    await f
      .Db.Dispatches.Where(x => x.Id == load.Id)
      .ExecuteUpdateAsync(x => x.SetProperty(d => d.Status, "in_transit"));

    var refused = await CloseAsync(
      f,
      await ActorAsync(f),
      load.Id,
      new(leg.Id, leg.Revision, Guid.NewGuid())
    );

    Assert.Equal(409, refused.StatusCode);
    Assert.Equal(SourceCancellation.Held, (await LegAsync(f, "1399")).Status);
  }

  private static async Task<Domain.Entities.Fleet.Truck> TruckAsync(
    DispatchSyncFixture f
  )
  {
    var truck = new Domain.Entities.Fleet.Truck
    {
      Id = Guid.NewGuid(),
      UnitNumber = "11005",
      ExternalId = "11005",
      IsActive = true,
    };
    f.Db.Trucks.Add(truck);
    await f.Db.SaveChangesAsync();
    return truck;
  }

  private static ExternalDispatch Source(string number, string status) =>
    new()
    {
      ExternalId = number,
      LoadNumber = int.Parse(number),
      Status = status,
      TruckNumber = "11005",
      Stops = new[] { "Pick Up", "Delivery" }
        .Select(
          (job, i) =>
            new ExternalDispatchStop
            {
              Sequence = i + 1,
              Job = job,
              Name = $"{number} visit {i}",
              Address = $"{i + 1} Main Road",
              City = "Syracuse",
              Province = "NY",
              Country = "US",
              TruckNumber = "11005",
            }
        )
        .ToList(),
    };

  private static async Task SyncAsync(DispatchSyncFixture f)
  {
    f.Memory.Compact(1);
    f.Db.ChangeTracker.Clear();
    var result = await f.Handler.Handle(new(), default);
    Assert.True(result.Success, string.Join(";", result.Errors ?? []));
    f.Db.ChangeTracker.Clear();
  }

  private static Task<Domain.Entities.Dispatch.Dispatch> LoadAsync(
    DispatchSyncFixture f,
    string number
  ) =>
    f
      .Db.Dispatches.AsNoTracking()
      .SingleAsync(x => x.LoadNumber == int.Parse(number));

  private static async Task<Domain.Entities.Execution.ExecutionLeg> LegAsync(
    DispatchSyncFixture f,
    string number
  )
  {
    var load = await LoadAsync(f, number);
    return await f
      .Db.ExecutionLegs.AsNoTracking()
      .Include(x => x.Stops)
      .SingleAsync(x => x.Loads.Any(l => l.DispatchId == load.Id));
  }

  private static async Task MovementAsync(
    DispatchSyncFixture f,
    Domain.Entities.Fleet.Truck truck,
    Guid leg
  )
  {
    f.Db.Movements.Add(
      new Movement
      {
        Id = Guid.NewGuid(),
        IdempotencyKey = Guid.NewGuid(),
        TruckId = truck.Id,
        ExecutionLegId = leg,
        Origin = "telemetry",
        StartedAt = DateTime.UtcNow.AddHours(-29),
        RecordedAt = DateTime.UtcNow,
        RecordedBy = Guid.NewGuid(),
      }
    );
    await f.Db.SaveChangesAsync();
    f.Db.ChangeTracker.Clear();
  }

  private static async Task<Domain.Models.Execution.TruckItinerarySnapshot> ItineraryAsync(
    DispatchSyncFixture f,
    Domain.Entities.Fleet.Truck truck
  )
  {
    var reader = new TruckItineraryReader(
      f.Db,
      new ExecutionReadScope(f.Db, new StageTimings()),
      new FleetNames(f.Db),
      new ActiveTransfers(f.Db)
    );
    return Assert.IsType<Domain.Models.Execution.TruckItinerarySnapshot>(
      await reader.ReadAsync(truck.Id, DateTimeOffset.UtcNow, default)
    );
  }

  private static async Task<User> ActorAsync(DispatchSyncFixture f)
  {
    var user = new User
    {
      Id = Guid.NewGuid(),
      IdentityUserId = "dispatcher",
      Name = "Dispatcher",
      Email = "dispatcher@example.invalid",
    };
    f.Db.Users.Add(user);
    await f.Db.SaveChangesAsync();
    f.Db.ChangeTracker.Clear();
    return user;
  }

  private static Task<Application.Models.RequestResponse<Application.Features.Execution.Models.ExecutionSourceApplyResult>> CloseAsync(
    DispatchSyncFixture f,
    User actor,
    Guid load,
    CloseCancelledExecutionRequest request
  )
  {
    f.Db.ChangeTracker.Clear();
    return new CloseCancelledExecutionHandler(
      f.Db,
      new Caller(actor.IdentityUserId),
      new Roles(),
      TimeProvider.System,
      f.Reads,
      TestCache.Preparation()
    ).Handle(new(load, request), default);
  }

  private sealed class Caller(string identity) : ICurrentUser
  {
    public bool IsAuthenticated => true;
    public string? IdentityUserId => identity;
  }

  private sealed class Roles : IUserRoleService
  {
    public Task<string?> GetAsync(string identityId, CancellationToken ct) =>
      Task.FromResult<string?>("Dispatch");

    public Task<Dictionary<Guid, string>> GetAsync(
      IReadOnlyCollection<Guid> ids,
      CancellationToken ct
    ) => throw new NotSupportedException();

    public Task SetAsync(
      string identityId,
      string role,
      CancellationToken ct
    ) => throw new NotSupportedException();
  }
}
