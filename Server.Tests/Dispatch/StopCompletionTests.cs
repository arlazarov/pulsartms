using Application.Features.Dispatch.Commands;
using Application.Features.Dispatch.Commands.SyncDispatche;
using Application.Features.Dispatch.Interfaces;
using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Queries;
using Application.Features.Routing.Services.FuelPlanning;
using Application.Features.Routing.Services.Routes;
using Domain.Entities.Fleet;
using Domain.Models.Eta;
using Domain.Models.Routing;
using Domain.Rules.Routing;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Server.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Integration")]
public sealed class StopCompletionTests
{
  [Fact]
  public async Task ConfirmationAndUndoPersistActorHistoryAndOnlyChangeTheSelectedVisit()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var before = RoutePlanInputs.Hash(f.Load, new());
    var mark = await f.Handler()
      .Handle(f.Command(0, f.Clock.GetUtcNow().AddHours(-1)), default);
    Assert.True(mark.Success);
    Assert.Equal(f.Actor.Id, mark.Response!.CompletedBy);
    Assert.Equal(f.Actor.Name, mark.Response.CompletedByName);
    Assert.Equal(1, mark.Response.Revision);
    Assert.Null(f.Load.Stops[0].PickedUpAt);
    var load = await f
      .Db.Dispatches.AsNoTracking()
      .Select(DispatchProjection.Details)
      .SingleAsync();
    Assert.True(load.Stops[0].IsCompleted);
    Assert.All(load.Stops.Skip(1), s => Assert.False(s.IsCompleted));
    Assert.NotEqual(before, RoutePlanInputs.Hash(f.Load, new()));
    Assert.True(f.Reads.Generation("dispatch") > 0);
    Assert.Equal(1, f.Queue.PendingCount);
    var undo = await f.Handler().Handle(f.Command(0, null), default);
    Assert.True(undo.Success);
    Assert.Null(undo.Response!.CompletedAt);
    Assert.False(f.Load.Stops[0].IsCompleted);
    Assert.NotEqual(before, RoutePlanInputs.Hash(f.Load, new()));
    var events = await f
      .Db.DispatchStopCompletionEvents.OrderBy(x => x.Revision)
      .ToArrayAsync();
    Assert.Equal(2, events.Length);
    Assert.All(events, e => Assert.Equal(f.Actor.Id, e.ActorId));
    Assert.NotNull(events[0].CompletedAt);
    Assert.Null(events[1].CompletedAt);
  }

  [Fact]
  public async Task ProviderPollingKeepsManualFactsAndLaterProviderCompletionSurvivesUndo()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    await f.Handler()
      .Handle(f.Command(0, f.Clock.GetUtcNow().AddHours(-1)), default);
    var source = new ExternalDispatch
    {
      LoadNumber = f.Load.LoadNumber,
      Status = "in_transit",
      Stops = f
        .Load.Stops.Select(s => new ExternalDispatchStop
        {
          Sequence = s.Sequence,
          Job = s.Job,
          Name = s.Name,
          Address = s.Address,
          City = s.City,
          Country = s.Country,
        })
        .ToList(),
    };
    DispatchImportTestData.Identify([source]);
    f.Db.DispatchSourceLinks.Add(
      new()
      {
        Provider = DispatchImportTestData.Key,
        ExternalId = source.ExternalId,
        DisplayName = DispatchImportTestData.DisplayName,
        DispatchId = f.Load.Id,
      }
    );
    await f.Db.SaveChangesAsync();
    using var memory = new MemoryCache(new MemoryCacheOptions());
    var sync = new SyncDispatchesCommandHandler(
      f.Db,
      [new Provider(source)],
      DispatchImportTestData.Options,
      f.Reads,
      memory,
      f.Queue
    );
    await sync.Handle(new(), default);
    Assert.NotNull(f.Load.Stops[0].ManualCompletedAt);
    Assert.Null(f.Load.Stops[0].PickedUpAt);
    source.Stops[0].PickedUpAt = f
      .Clock.GetUtcNow()
      .AddMinutes(-30)
      .UtcDateTime;
    await sync.Handle(new(), default);
    Assert.True(
      (await f.Handler().Handle(f.Command(0, null), default)).Success
    );
    Assert.True(f.Load.Stops[0].IsCompleted);
    Assert.Equal(source.Stops[0].PickedUpAt, f.Load.Stops[0].PickedUpAt);
    Assert.Equal(2, await f.Db.DispatchStopCompletionEvents.CountAsync());
  }

  [Theory]
  [InlineData("Admin", true, true, 200)]
  [InlineData("Dispatch", true, true, 200)]
  [InlineData("Viewer", true, true, 403)]
  [InlineData(null, true, true, 403)]
  [InlineData("Admin", false, true, 401)]
  [InlineData("Admin", true, false, 403)]
  public async Task OnlyActiveAuthenticatedOperatorsCanConfirm(
    string? role,
    bool authenticated,
    bool active,
    int status
  )
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    f.Actor.IsActive = active;
    await f.Db.SaveChangesAsync();
    var result = await f.Handler(role, authenticated)
      .Handle(f.Command(0, f.Clock.GetUtcNow().AddHours(-1)), default);
    Assert.Equal(status, result.StatusCode);
    Assert.Equal(
      status == 200 ? 1 : 0,
      await f.Db.DispatchStopCompletionEvents.CountAsync()
    );
  }

  [Fact]
  public async Task StaleRevisionWrongLoadAndChangedStopIdentityCannotSave()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var stale = f.Command(0, f.Clock.GetUtcNow().AddHours(-1));
    Assert.Equal(
      404,
      (
        await f.Handler()
          .Handle(stale with { DispatchId = Guid.NewGuid() }, default)
      ).StatusCode
    );
    Assert.Equal(
      409,
      (
        await f.Handler()
          .Handle(
            stale with
            {
              Update = stale.Update with
              {
                CompletionIdentity = new string('a', 64),
              },
            },
            default
          )
      ).StatusCode
    );
    Assert.True((await f.Handler().Handle(stale, default)).Success);
    Assert.Equal(409, (await f.Handler().Handle(stale, default)).StatusCode);
    Assert.Equal(1, await f.Db.DispatchStopCompletionEvents.CountAsync());
  }

  [Fact]
  public async Task ActualTimeCannotBeFutureBeforeArrivalOrReplaceProviderCompletion()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    f.Load.Stops[0].ArrivedAt = f.Clock.GetUtcNow().AddHours(-2).UtcDateTime;
    await f.Db.SaveChangesAsync();
    Assert.False(
      (
        await f.Handler()
          .Handle(f.Command(0, f.Clock.GetUtcNow().AddMinutes(1)), default)
      ).Success
    );
    Assert.False(
      (
        await f.Handler()
          .Handle(f.Command(0, f.Clock.GetUtcNow().AddHours(-3)), default)
      ).Success
    );
    f.Load.Stops[0].PickedUpAt = f.Clock.GetUtcNow().AddHours(-1).UtcDateTime;
    await f.Db.SaveChangesAsync();
    Assert.Equal(
      409,
      (
        await f.Handler().Handle(f.Command(0, f.Clock.GetUtcNow()), default)
      ).StatusCode
    );
    Assert.Empty(await f.Db.DispatchStopCompletionEvents.ToListAsync());
  }

  [Fact]
  public async Task TrackerDoesNotCompleteOtherVisitsAndUndoWinsOverOldGpsInference()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    await f.Handler()
      .Handle(f.Command(2, f.Clock.GetUtcNow().AddHours(-1)), default);
    var stops = f
      .Load.Stops.Select(s => new PlanStop(
        s.Id,
        s.Name,
        s.Address,
        s.Sequence,
        new(40, -80)
      ))
      .ToList();
    var plan = new RoutePlan { Stops = stops };
    RouteStopTracker.Update(
      plan,
      f.Load,
      null,
      f.Clock.GetUtcNow().UtcDateTime
    );
    Assert.Equal([stops[2].Id], plan.Tracking.PassedStopIds);
    Assert.Equal(stops[0].Id, plan.Tracking.NextStopId);
    plan.Tracking.VisitedStops[stops[2].Id] = f
      .Clock.GetUtcNow()
      .AddHours(-2)
      .UtcDateTime;
    await f.Handler().Handle(f.Command(2, null), default);
    RouteStopTracker.Update(
      plan,
      f.Load,
      null,
      f.Clock.GetUtcNow().UtcDateTime
    );
    Assert.Empty(plan.Tracking.PassedStopIds);
  }

  [Fact]
  public async Task ConcurrencyConflictRollsBackTheCompetingAuditInsert()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    await using var stale = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlite(f.Db.Database.GetDbConnection())
        .Options
    );
    await stale
      .DispatchStops.Include(s => s.Dispatch)
      .SingleAsync(s => s.Id == f.Load.Stops[0].Id);
    var first = f.Command(0, f.Clock.GetUtcNow().AddHours(-1));
    Assert.True((await f.Handler().Handle(first, default)).Success);
    var result = await f.Handler(db: stale)
      .Handle(
        first with
        {
          Update = first.Update with
          {
            CompletedAt = f.Clock.GetUtcNow().AddMinutes(-30),
          },
        },
        default
      );
    Assert.Equal(409, result.StatusCode);
    Assert.Equal(1, await f.Db.DispatchStopCompletionEvents.CountAsync());
    Assert.Equal(
      first.Update.CompletedAt!.Value.UtcDateTime,
      (
        await f
          .Db.DispatchStops.AsNoTracking()
          .SingleAsync(s => s.Id == first.StopId)
      ).ManualCompletedAt
    );
  }

  [Fact]
  public async Task FullyManuallyCompletedLoadMovesToHistoryAndReturnsOnUndo()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    using var services = new PlanningTestServices(f.Db, reads: f.Reads);
    foreach (var i in Enumerable.Range(0, 5))
      Assert.True(
        (
          await f.Handler()
            .Handle(f.Command(i, f.Clock.GetUtcNow().AddHours(-1)), default)
        ).Success
      );
    var active = await services.Board.Handle(
      new(IncludeHos: false, IncludeFinancials: false, IncludeEta: false),
      default
    );
    Assert.DoesNotContain(
      active.Response!.Items.SelectMany(x => x.Dispatches),
      x => x.Id == f.Load.Id
    );
    var history = await new GetDispatchQueryHandler(
      f.Db,
      services.Deadheads
    ).Handle(new(Status: "completed"), default);
    Assert.Contains(history.Response!.Items, x => x.Id == f.Load.Id);
    await f.Handler().Handle(f.Command(4, null), default);
    var restored = await services.Board.Handle(
      new(IncludeHos: false, IncludeFinancials: false, IncludeEta: false),
      default
    );
    Assert.Contains(
      restored.Response!.Items.SelectMany(x => x.Dispatches),
      x => x.Id == f.Load.Id
    );
  }

  [Fact]
  public async Task FuelSignaturesAndEtaActivityRespectManualChangesWithoutProviderWrites()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var truck = new Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = "completion-fuel",
      UnitNumber = "fixture",
    };
    f.Db.Trucks.Add(truck);
    f.Load.TruckId = truck.Id;
    await f.Db.SaveChangesAsync();
    async Task<DispatchResponse> Read() =>
      await f
        .Db.Dispatches.AsNoTracking()
        .Select(DispatchProjection.Details)
        .SingleAsync();
    var before = await Read();
    var original = FuelWorkSignature.LoadSignature(before);
    await f.Handler()
      .Handle(f.Command(0, f.Clock.GetUtcNow().AddHours(-1)), default);
    var after = await Read();
    Assert.NotEqual(original, FuelWorkSignature.LoadSignature(after));
    Assert.True(
      new EtaStopActivity(
        null,
        null,
        null,
        null,
        after.Stops[0].ManualCompletedAt
      ).Completed
    );
    var itinerary = after
      .Stops.Skip(1)
      .Select(s => new FuelItineraryStop(
        after.Id,
        new(s.Id, s.Name, s.Address, s.Sequence, new(40, -80)),
        s.Sequence * 10
      ))
      .ToArray();
    Assert.True(
      FuelPlanProjection.RemainingStopsMatch(
        itinerary,
        after.Id,
        after.Stops[1].Id,
        [after]
      )
    );
    Assert.Null(after.Stops[0].DepartedAt);
    await f.Handler().Handle(f.Command(0, null), default);
    Assert.NotEqual(original, FuelWorkSignature.LoadSignature(await Read()));
  }

  private sealed class Provider(ExternalDispatch load) : IDispatchProvider
  {
    public string Key => DispatchImportTestData.Key;
    public string DisplayName => DispatchImportTestData.DisplayName;

    public Task<IReadOnlyList<ExternalDispatch>> GetDispatchesAsync(
      CancellationToken ct = default
    ) => Task.FromResult(DispatchImportTestData.Identify([load]));

    public Task<IReadOnlyList<ExternalDispatch>> GetDispatchesAsync(
      DateOnly from,
      DateOnly to,
      CancellationToken ct = default
    ) => GetDispatchesAsync(ct);
  }
}
