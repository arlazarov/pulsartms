using Application.Caching;
using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Queries;
using Application.Features.Execution.Services;
using Application.Features.Routing.Background;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Queries;
using Application.Features.Routing.Services.Addresses;
using Application.Features.Routing.Services.Deadheads;
using Application.Features.Routing.Services.FuelPlanning;
using Application.Features.Routing.Services.Routes;
using Application.Interfaces;
using Domain.Entities.Dispatch;
using Domain.Entities.Execution;
using Domain.Entities.Fleet;
using Domain.Models.Execution;
using Domain.Models.Routing;
using Domain.Policies;
using Domain.Rules;
using Domain.Rules.Routing;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Server.Tests.Routing;

using Application.Features.Execution.Models;
using Dispatch = global::Domain.Entities.Dispatch.Dispatch;

[Trait("Category", "Routing")]
[Trait("Kind", "Integration")]
public sealed class BaseRouteOperationTests
{
  [Fact]
  public async Task PersistedFutureDemandRunsWithAnEmptyReplacementWorkerQueue()
  {
    await using var fixture = await Fixture.CreateAsync();
    var id = await fixture.AddAsync(30);
    await using (var scope = fixture.Root.CreateAsyncScope())
      await scope
        .ServiceProvider.GetRequiredService<ISourceRoadStore>()
        .DemandAsync(
          id,
          "future",
          0,
          fixture.Clock.GetUtcNow().UtcDateTime,
          default
        );
    var options = Options.Create(new RoutePreparationOptions());
    var replacement = new BaseRouteOperation(
      fixture.Root.GetRequiredService<IServiceScopeFactory>(),
      NullLogger<BaseRouteOperation>.Instance,
      new RoutePreparationQueue(options, fixture.Clock),
      options,
      fixture.Clock
    );
    await replacement.RunOnceAsync(default);
    Assert.Equal(1, fixture.Router.Calls);
    Assert.Equal(0, await fixture.PendingAsync());
  }

  [Fact]
  public async Task DeletedWorkDemandIsRetiredWithoutProviderCalls()
  {
    await using var fixture = await Fixture.CreateAsync();
    await using (var scope = fixture.Root.CreateAsyncScope())
      await scope
        .ServiceProvider.GetRequiredService<ISourceRoadStore>()
        .DemandAsync(
          Guid.NewGuid(),
          "deleted",
          0,
          fixture.Clock.GetUtcNow().UtcDateTime,
          default
        );
    await fixture.Operation.RunOnceAsync(default);
    Assert.Equal(0, fixture.Router.Calls);
    Assert.Equal(0, await fixture.PendingAsync());
  }

  [Fact]
  public async Task InputIdentityIsIndependentOfBatchAndProcessCacheGenerations()
  {
    await using var fixture = await Fixture.CreateAsync();
    var ids = new[]
    {
      await fixture.AddAsync(0, "in_transit"),
      await fixture.AddAsync(1),
      await fixture.AddAsync(2),
    };
    await using var scope = fixture.Root.CreateAsyncScope();
    var reader = scope.ServiceProvider.GetRequiredService<SourceRoadInputs>();
    var batch = await reader.ReadAsync(ids, default);
    fixture.Reads.Invalidate($"profile:{fixture.Truck.Id}");
    foreach (var id in ids)
      Assert.Equal(batch[id], await reader.ReadAsync(id, default));
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task PendingAddressVerificationRetainsOnlyReliableSourcePoints(
    bool locallyEdited
  )
  {
    await using var fixture = await Fixture.CreateAsync();
    await fixture.AddAsync(0, "in_transit");
    var next = await fixture.AddAsync(1);
    await using (var edit = fixture.Root.CreateAsyncScope())
    {
      var db = edit.ServiceProvider.GetRequiredService<AppDbContext>();
      var stop = await db.DispatchStops.SingleAsync(x =>
        x.DispatchId == next && x.Sequence == 1
      );
      stop.Address = "SOUTH BAY, FL, USA, 33493";
      stop.City = "SOUTH BAY";
      stop.SourceAddressJson = StopAddress.From(stop).Serialize();
      stop.AddressRetryAfter = DateTime.UtcNow.AddHours(12);
      if (locallyEdited)
        stop.Address = "99 New Road";
      await db.SaveChangesAsync();
    }
    await fixture.Operation.RunOnceAsync(default);
    Assert.Equal(locallyEdited ? 1 : 3, fixture.Router.Calls);
    Assert.Equal(locallyEdited ? 1 : 0, await fixture.PendingAsync());
    Assert.Equal(0, fixture.Router.GeocodeCalls);
    if (locallyEdited)
    {
      await using var edit = fixture.Root.CreateAsyncScope();
      var db = edit.ServiceProvider.GetRequiredService<AppDbContext>();
      var stop = await db.DispatchStops.SingleAsync(x =>
        x.DispatchId == next && x.Sequence == 1
      );
      stop.AddressVerifiedAt = DateTime.UtcNow.AddMinutes(-1);
      stop.AddressRetryAfter = null;
      stop.Latitude += .01m;
      await db.SaveChangesAsync();
    }
    await fixture.Operation.RunOnceAsync(default);
    Assert.Equal(3, fixture.Router.Calls);
    Assert.Equal(0, fixture.Router.GeocodeCalls);
    Assert.Equal(0, await fixture.PendingAsync());
    await using var check = fixture.Root.CreateAsyncScope();
    var saved = await check
      .ServiceProvider.GetRequiredService<AppDbContext>()
      .DispatchDeadheads.SingleAsync();
    Assert.Equal(next, saved.DispatchId);
    Assert.NotNull(saved.RouteJson);
    Assert.Equal(100m, saved.Miles);
  }

  [Theory]
  [InlineData("Drop Off", false, false)]
  [InlineData("Drop Off", false, true)]
  [InlineData("Delivery", false, false)]
  [InlineData("Delivery", false, true)]
  [InlineData("Drop Off", true, false)]
  [InlineData("Drop Off", true, true)]
  public async Task IncomingNativeTruckPreparesItsSuccessor(
    string deliveryOperation,
    bool duplicatePlanned,
    bool nativeSuccessor
  )
  {
    await using var fixture = await Fixture.CreateAsync();
    var (current, legs, _) = await fixture.AddTransferAsync();
    var incoming = legs[1];
    var next = await fixture.AddAsync(15);
    Guid? nextLeg = null;
    await using (var edit = fixture.Root.CreateAsyncScope())
    {
      var db = edit.ServiceProvider.GetRequiredService<AppDbContext>();
      var load = await db
        .Dispatches.Include(x => x.Stops)
        .SingleAsync(x => x.Id == next);
      load.TruckId = incoming.TruckId;
      foreach (var stop in load.Stops)
        stop.TruckId = incoming.TruckId;
      var native = await db.ExecutionLegs.SingleAsync(x => x.Id == incoming.Id);
      var stops = ExecutionStopRows.Read(native);
      stops[^1].Job = deliveryOperation;
      ExecutionStopRows.Replace(native, stops);
      if (duplicatePlanned)
      {
        var planned = new ExecutionLeg
        {
          Id = Guid.NewGuid(),
          TripId = native.TripId,
          TruckId = native.TruckId,
          Status = "planned",
          Revision = 1,
          Stops = ExecutionStopRows.Capture(ExecutionStopRows.Read(native)),
        };
        db.ExecutionLegs.Add(planned);
        db.LoadExecutionLegs.Add(
          new()
          {
            Id = Guid.NewGuid(),
            DispatchId = current,
            ExecutionLegId = planned.Id,
            Sequence = 2,
          }
        );
      }
      if (nativeSuccessor)
      {
        var accepted = new ExecutionLeg
        {
          Id = Guid.NewGuid(),
          Trip = new Trip { Id = Guid.NewGuid() },
          TruckId = incoming.TruckId,
          Status = "planned",
          Revision = 1,
          Stops = ExecutionStopRows.Capture(load.Stops),
          Loads =
          [
            new()
            {
              Id = Guid.NewGuid(),
              DispatchId = next,
              Sequence = 1,
              StartVisitId = load.Stops[0].Id,
              EndVisitId = load.Stops[^1].Id,
            },
          ],
        };
        nextLeg = accepted.Id;
        db.ExecutionLegs.Add(accepted);
      }
      await db.SaveChangesAsync();
    }
    fixture.Queue.Request(next, incoming.TruckId);
    await fixture.Operation.RunOnceAsync(default);
    await using var scope = fixture.Root.CreateAsyncScope();
    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var services =
      scope.ServiceProvider.GetRequiredService<PlanningTestServices>();
    var saved = await context.DispatchDeadheads.SingleAsync();
    Assert.Equal(next, saved.DispatchId);
    Assert.Equal(nextLeg, saved.ExecutionLegId);
    Assert.Equal(current, saved.PreviousDispatchId);
    Assert.Equal(incoming.Id, saved.PreviousExecutionLegId);
    Assert.Equal(100m, saved.Miles);
    var loadToRead = await services.Routes.LoadAsync(next, default, nextLeg);
    var profile = await services.Routes.ProfileAsync(incoming.TruckId, default);
    Assert.NotNull(
      await services.Deadheads.ReadRouteAsync(
        current,
        loadToRead,
        profile,
        default
      )
    );
    var originalHash = saved.InputHash;
    var leg = await context.ExecutionLegs.SingleAsync(x => x.Id == incoming.Id);
    leg.Revision++;
    await context.SaveChangesAsync();
    Assert.Null(
      await services.Deadheads.ReadRouteAsync(
        current,
        loadToRead,
        profile,
        default
      )
    );
    await services.Deadheads.EnsureAsync(loadToRead, profile, default);
    Assert.NotEqual(originalHash, saved.InputHash);
    Assert.NotNull(
      await services.Deadheads.ReadRouteAsync(
        current,
        loadToRead,
        profile,
        default
      )
    );
    leg.Status = "completed";
    await context.SaveChangesAsync();
    Assert.NotNull(
      await services.Deadheads.ReadRouteAsync(
        current,
        loadToRead,
        profile,
        default
      )
    );
  }

  [Fact]
  public async Task AllUpcomingAssignmentsBeyondHorizonPrepareFuelConnections()
  {
    await using var fixture = await Fixture.CreateAsync();
    var current = await fixture.AddAsync(0, "in_transit");
    var next = await fixture.AddAsync(15);
    var third = await fixture.AddAsync(20);
    await fixture.Operation.RunOnceAsync(default);
    Assert.Equal(1, fixture.Router.Calls);

    for (var attempt = 0; attempt < 2; attempt++)
      foreach (var id in new[] { next, third })
        fixture.Queue.Request(id, fixture.Truck.Id);
    Assert.Equal(1, fixture.Router.Calls);
    Assert.Equal(2, fixture.Queue.PendingCount);
    await fixture.Operation.RunOnceAsync(default);
    Assert.Equal(5, fixture.Router.Calls);
    Assert.Equal(0, await fixture.PendingAsync());

    await using var scope = fixture.Root.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var planning =
      scope.ServiceProvider.GetRequiredService<PlanningTestServices>();
    var load = await planning.Routes.LoadAsync(next, default);
    var profile = await planning.Routes.ProfileAsync(fixture.Truck.Id, default);
    var connection = await planning.Deadheads.ReadRouteAsync(
      current,
      load,
      profile,
      default
    );
    Assert.NotNull(connection);
    Assert.Equal(100, connection.Miles);
    Assert.Equal(3, await db.DispatchBaseRoutes.CountAsync());
    Assert.Equal(2, await db.DispatchDeadheads.CountAsync());

    fixture.Queue.Request(next, fixture.Truck.Id);
    fixture.Queue.Request(third, fixture.Truck.Id);
    await fixture.Operation.RunOnceAsync(default);
    Assert.Equal(5, fixture.Router.Calls);
    Assert.Equal(0, await fixture.PendingAsync());

    var plan = await planning.Routes.BuildAsync(current, new(profile), default);
    plan.Tracking.PassedStopIds.Add(plan.Stops[0].Id);
    var board = await planning.Board.Handle(
      new(
        TruckId: fixture.Truck.Id,
        IncludeHos: false,
        IncludeFinancials: false,
        IncludeEta: false
      ),
      default
    );
    var progress = new RouteProgress(
      0,
      plan.Route.Miles,
      plan.Route.Seconds,
      0,
      false,
      false,
      DateTime.UtcNow,
      plan.Route.Legs[0].Points[0]
    );
    var state = new RoutePlanningState(profile, plan, progress, 50, null, true);
    var fuel = await new FuelHorizon(
      planning.FuelInputs,
      db,
      planning.Deadheads
    ).BuildAsync(state, profile, default);
    Assert.Equal(new[] { current, next, third }, fuel.DispatchIds);
    Assert.Equal(5, fuel.Stops.Count);
    Assert.Equal(5, fixture.Router.Calls);
  }

  [Fact]
  public async Task CompletedFutureDemandRepairsChangedAddressWithoutAnotherPoll()
  {
    await using var fixture = await Fixture.CreateAsync();
    await fixture.AddAsync(0, "in_transit");
    var next = await fixture.AddAsync(15);
    fixture.Queue.Request(next, fixture.Truck.Id);
    await fixture.Operation.RunOnceAsync(default);
    Assert.Equal(3, fixture.Router.Calls);
    await using (var edit = fixture.Root.CreateAsyncScope())
    {
      var db = edit.ServiceProvider.GetRequiredService<AppDbContext>();
      var stop = await db.DispatchStops.SingleAsync(x =>
        x.DispatchId == next && x.Sequence == 1
      );
      stop.Latitude += .01m;
      await db.SaveChangesAsync();
    }
    fixture.Queue.AddressesChanged([next], [fixture.Truck.Id]);
    await fixture.Operation.RunOnceAsync(default);
    Assert.Equal(5, fixture.Router.Calls);
    Assert.Equal(0, await fixture.PendingAsync());
    fixture.Queue.Request(next, fixture.Truck.Id);
    await fixture.Operation.RunOnceAsync(default);
    Assert.Equal(5, fixture.Router.Calls);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task PreparesBothSidesOfACompletedTransferWithoutChangingLiveProgressOrHistory(
    bool archived
  )
  {
    await using var fixture = await Fixture.CreateAsync();
    var (id, legs, participant) = await fixture.AddTransferAsync(archived);
    if (archived)
    {
      await fixture.Operation.RunOnceAsync(default);
      Assert.Equal(0, fixture.Router.Calls);
      await using var demand = fixture.Root.CreateAsyncScope();
      var context = demand.ServiceProvider.GetRequiredService<AppDbContext>();
      var planning =
        demand.ServiceProvider.GetRequiredService<PlanningTestServices>();
      var reader = new GetDispatchMapRouteHandler(
        context,
        planning.Routes,
        new TruckPlanningProfileService(
          context,
          fixture.Reads,
          planning.Settings,
          planning.ExchangeRates
        ),
        new SourceRoadDemand(new SourceRoadStore(context), fixture.Clock)
      );
      for (var i = 0; i < 2; i++)
        Assert.Equal(
          2,
          (await reader.Handle(new(id), default)).Response!.MissingSections
        );
      Assert.Equal(0, fixture.Router.Calls);
      Assert.Equal(1, await fixture.PendingAsync());
    }
    await fixture.Operation.RunOnceAsync(default);
    Assert.Equal(2, fixture.Router.Calls);
    await fixture.Operation.RunOnceAsync(default);
    Assert.Equal(2, fixture.Router.Calls);
    Assert.Equal(0, await fixture.PendingAsync());
    await using var scope = fixture.Root.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var services =
      scope.ServiceProvider.GetRequiredService<PlanningTestServices>();
    var result = (
      await new GetDispatchMapRouteHandler(
        db,
        services.Routes,
        new TruckPlanningProfileService(
          db,
          fixture.Reads,
          services.Settings,
          services.ExchangeRates
        ),
        new SourceRoadDemand(new SourceRoadStore(db), fixture.Clock)
      ).Handle(new(id), default)
    ).Response!;
    Assert.Equal(0, result.MissingSections);
    Assert.Equal(2, result.Segments.Count);
    Assert.Equal(participant.ReleaseVisitId, result.Segments[0].ToStopId);
    Assert.Equal(participant.ReceiveVisitId, result.Segments[1].FromStopId);
    Assert.DoesNotContain(
      result.Segments,
      x => x.FromStopId == participant.ReleaseVisitId
    );
    Assert.Equal(
      "{\"fromCurrentPosition\":true,\"stops\":[]}",
      (await db.DispatchRoutePlans.SingleAsync()).PlanJson
    );
    foreach (var leg in legs)
    {
      var saved = await db.ExecutionLegs.SingleAsync(x => x.Id == leg.Id);
      Assert.Equal(leg.Revision, saved.Revision);
      Assert.Equal(leg.Status, saved.Status);
      Assert.Equal(
        ExecutionSnapshots.Write(ExecutionStopRows.Read(leg)),
        ExecutionSnapshots.Write(ExecutionStopRows.Read(saved))
      );
    }
    var transfer = await db.SwitchParticipants.SingleAsync();
    Assert.Equal(participant.ReleasedAt, transfer.ReleasedAt);
    Assert.Equal(participant.ReceivedAt, transfer.ReceivedAt);
    Assert.Equal(participant.Revision, transfer.Revision);
  }

  [Fact]
  public async Task DoesNotRouteAcrossAChangedTransferLocation()
  {
    await using var fixture = await Fixture.CreateAsync();
    var (id, legs, _) = await fixture.AddTransferAsync();
    await using var scope = fixture.Root.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var source = await db.Dispatches.SingleAsync(x => x.Id == id);
    var stops = ExecutionStopRows.Read(legs[1]);
    var load = RouteWorkProjection.Capture(source, legs[1], stops);
    var visit = await db.ExecutionLegStops.SingleAsync(x =>
      x.ExecutionLegId == legs[1].Id && x.Id == stops[0].Id
    );
    visit.Latitude += 1;
    await db.SaveChangesAsync();

    await Assert.ThrowsAsync<RoutePlanningException>(
      () =>
        StopLocation.ResolveAsync(
          load.Id,
          load.ExecutionLegId,
          load.Stops,
          db,
          fixture.Router,
          default
        )
    );

    Assert.Equal(0, fixture.Router.Calls);
    Assert.Equal(0, fixture.Router.GeocodeCalls);
  }

  [Theory]
  [InlineData("cancelled")]
  [InlineData("other-load")]
  [InlineData("other-leg")]
  [InlineData("legacy")]
  public async Task ExplicitLocationsCannotLeakAcrossTransferOwnership(
    string mode
  )
  {
    await using var fixture = await Fixture.CreateAsync();
    var (id, legs, _) = await fixture.AddTransferAsync();
    await using var scope = fixture.Root.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var load = RouteWorkProjection.Capture(
      await db.Dispatches.SingleAsync(x => x.Id == id),
      legs[0],
      ExecutionStopRows.Read(legs[0])
    );
    var boundary = load.Stops.Last();
    if (mode == "cancelled")
    {
      (await db.SwitchParticipants.SingleAsync()).IsCancelled = true;
      await db.SaveChangesAsync();
    }
    if (mode == "other-load")
      load = load with { Id = Guid.NewGuid() };
    if (mode == "other-leg")
      load = load with { ExecutionLegId = legs[1].Id };
    if (mode == "legacy")
      load = load with { ExecutionLegId = null };
    await Assert.ThrowsAsync<InvalidOperationException>(
      () =>
        StopLocation.ResolveAsync(
          load.Id,
          load.ExecutionLegId,
          [boundary],
          db,
          fixture.Router,
          default
        )
    );
    Assert.Equal(1, fixture.Router.GeocodeCalls);
  }

  [Fact]
  public async Task ConcurrentAssignmentChangeRejectsTheOldRoadAndRetriesTheNewRevision()
  {
    await using var fixture = await Fixture.CreateAsync();
    var (_, legs, _) = await fixture.AddTransferAsync();
    fixture.Router.BeforeCalculate = async (_, ct) =>
    {
      fixture.Router.BeforeCalculate = null;
      await using var scope = fixture.Root.CreateAsyncScope();
      var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
      (
        await db.ExecutionLegs.SingleAsync(x => x.Id == legs[1].Id, ct)
      ).Revision++;
      await db.SaveChangesAsync(ct);
    };
    await fixture.Operation.RunOnceAsync(default);
    await using (var scope = fixture.Root.CreateAsyncScope())
    {
      var routes = await scope
        .ServiceProvider.GetRequiredService<AppDbContext>()
        .DispatchBaseRoutes.ToListAsync();
      Assert.Equal(legs[0].Id, Assert.Single(routes).ExecutionLegId);
    }
    await fixture.Operation.RunOnceAsync(default);
    Assert.Equal(3, fixture.Router.Calls);
    Assert.Equal(0, await fixture.PendingAsync());
    await using var check = fixture.Root.CreateAsyncScope();
    Assert.Equal(
      2,
      await check
        .ServiceProvider.GetRequiredService<AppDbContext>()
        .DispatchBaseRoutes.CountAsync()
    );
  }

  [Fact]
  public async Task UnchangedScansDoNotPrepareAgainAndFinancialChangesReuseTheRoad()
  {
    await using var fixture = await Fixture.CreateAsync();
    var id = await fixture.AddAsync(0);
    await fixture.Operation.RunOnceAsync(default);
    Assert.Equal(1, fixture.Router.Calls);
    await fixture.Operation.RunOnceAsync(default);
    Assert.Equal(1, fixture.Router.Calls);
    Assert.Equal(0, await fixture.PendingAsync());
    await using (var scope = fixture.Root.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
      (await db.Dispatches.SingleAsync(x => x.Id == id)).Price = 200;
      await db.SaveChangesAsync();
    }
    await fixture.Operation.RunOnceAsync(default);
    Assert.Equal(1, fixture.Router.Calls);
    await using var check = fixture.Root.CreateAsyncScope();
    Assert.Equal(
      2,
      (
        await check
          .ServiceProvider.GetRequiredService<AppDbContext>()
          .DispatchRates.SingleAsync()
      ).LoadedRatePerMile
    );
  }

  [Fact]
  public async Task ExplicitDemandBypassesTheSpeculativeHorizonWithoutRepeatingItOnEveryPoll()
  {
    await using var fixture = await Fixture.CreateAsync();
    var id = await fixture.AddAsync(30);
    await fixture.AddAsync(30, "unassigned");
    await fixture.Operation.RunOnceAsync(default);
    Assert.Equal(0, fixture.Router.Calls);
    fixture.Queue.Request(id, "missing-road");
    await fixture.Operation.RunOnceAsync(default);
    Assert.Equal(1, fixture.Router.Calls);
    fixture.Queue.Request(id, "missing-road");
    await fixture.Operation.RunOnceAsync(default);
    Assert.Equal(1, fixture.Router.Calls);
    Assert.Equal(0, await fixture.PendingAsync());
  }

  [Fact]
  public async Task ProfileChangeDuringProviderWorkCannotBeAcknowledgedByTheOldAttempt()
  {
    await using var fixture = await Fixture.CreateAsync();
    var id = await fixture.AddAsync(0);
    fixture.Router.BeforeCalculate = async (_, ct) =>
    {
      fixture.Router.BeforeCalculate = null;
      await using var scope = fixture.Root.CreateAsyncScope();
      var services =
        scope.ServiceProvider.GetRequiredService<PlanningTestServices>();
      await services.Routes.SaveProfileAsync(
        id,
        new() { UsesFleetDefaults = true, HeightFeet = 14 },
        ct
      );
    };
    await fixture.Operation.RunOnceAsync(default);
    Assert.Equal(new[] { 13.5 }, fixture.Router.Heights);
    await fixture.Operation.RunOnceAsync(default);
    Assert.Equal(new[] { 13.5, 14 }, fixture.Router.Heights);
    Assert.Equal(0, await fixture.PendingAsync());
  }

  [Fact]
  public async Task InitialWorkerScanStartsImmediatelyAndCancellationDoesNotStrandTheUntakenBatch()
  {
    await using var fixture = await Fixture.CreateAsync();
    await fixture.AddAsync(0);
    await fixture.AddAsync(1);
    var started = new TaskCompletionSource(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    fixture.Router.BeforeCalculate = async (_, ct) =>
    {
      started.TrySetResult();
      await Task.Delay(Timeout.InfiniteTimeSpan, ct);
    };
    using var cancellation = new CancellationTokenSource();
    var running = fixture.Operation.RunAsync(cancellation.Token);
    await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
    await cancellation.CancelAsync();
    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
    Assert.Equal(2, await fixture.PendingAsync());
    await using var scope = fixture.Root.CreateAsyncScope();
    var requests = await scope
      .ServiceProvider.GetRequiredService<AppDbContext>()
      .SourceRoadRequests.AsNoTracking()
      .ToListAsync();
    var runningRequest = Assert.Single(requests, x => x.LeaseId != null);
    var waiting = Assert.Single(requests, x => x.LeaseId == null);
    Assert.Equal(
      waiting.DispatchId,
      (await fixture.ClaimAsync(TimeSpan.FromMinutes(10)))!.DispatchId
    );
    fixture.Clock.Advance(TimeSpan.FromMinutes(3));
    Assert.Equal(
      runningRequest.DispatchId,
      (await fixture.ClaimAsync())!.DispatchId
    );
  }

  [Fact]
  public async Task RepairPagesReachLaterLoadsAndStopOnlyAssignmentsStayDeduplicated()
  {
    await using var fixture = await Fixture.CreateAsync(
      new() { ScanPageSize = 2, BatchSize = 10 }
    );
    await fixture.AddAsync(0, stopOnly: true);
    await fixture.AddAsync(1);
    await fixture.AddAsync(2);
    await fixture.Operation.RunOnceAsync(default);
    await fixture.Operation.RunOnceAsync(default);
    var calls = fixture.Router.Calls;
    Assert.True(calls >= 3);
    await fixture.Operation.RunOnceAsync(default);
    await fixture.Operation.RunOnceAsync(default);
    Assert.Equal(calls, fixture.Router.Calls);
    Assert.Equal(0, await fixture.PendingAsync());
    await using var scope = fixture.Root.CreateAsyncScope();
    Assert.Equal(
      3,
      await scope
        .ServiceProvider.GetRequiredService<AppDbContext>()
        .DispatchBaseRoutes.CountAsync()
    );
  }

  [Fact]
  public async Task GeometryRepairWithRetainedMileageRemainsPendingUntilItsPersistedRetryDeadline()
  {
    await using var fixture = await Fixture.CreateAsync();
    await fixture.AddAsync(0);
    var id = await fixture.AddAsync(1);
    await fixture.Operation.RunOnceAsync(default);
    var calls = fixture.Router.Calls;
    var retryAfter = DateTime.UtcNow.AddMinutes(20);
    await using (var scope = fixture.Root.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
      var saved = await db.DispatchDeadheads.SingleAsync(x =>
        x.DispatchId == id
      );
      Assert.Equal(100m, saved.Miles);
      saved.RouteJson = null;
      saved.RetryAfter = retryAfter;
      await db.SaveChangesAsync();
    }
    fixture.Queue.MarkDirty(id);
    await fixture.Operation.RunOnceAsync(default);
    Assert.Equal(calls, fixture.Router.Calls);
    Assert.Equal(1, await fixture.PendingAsync());
    Assert.Null(await fixture.ClaimAsync());
    await fixture.Operation.RunOnceAsync(default);
    Assert.Equal(calls, fixture.Router.Calls);
    Assert.Equal(1, await fixture.PendingAsync());
    await using var check = fixture.Root.CreateAsyncScope();
    var pending = await check
      .ServiceProvider.GetRequiredService<AppDbContext>()
      .DispatchDeadheads.AsNoTracking()
      .SingleAsync(x => x.DispatchId == id);
    Assert.Equal(100m, pending.Miles);
    Assert.Equal(retryAfter, pending.RetryAfter);
  }

  private sealed class Fixture(
    SqliteConnection connection,
    ServiceProvider root,
    ReadCache reads,
    RoutePreparationQueue queue,
    Router router,
    ManualTimeProvider clock,
    Truck truck,
    BaseRouteOperation operation
  ) : IAsyncDisposable
  {
    private int loadNumber;
    public ManualTimeProvider Clock => clock;
    public ServiceProvider Root => root;

    public async Task<int> PendingAsync()
    {
      await using var scope = root.CreateAsyncScope();
      return await scope
        .ServiceProvider.GetRequiredService<AppDbContext>()
        .SourceRoadRequests.CountAsync(x =>
          x.RequestedVersion > x.CompletedVersion
        );
    }

    public async Task<SourceRoadWork?> ClaimAsync(TimeSpan? lease = null)
    {
      await using var scope = root.CreateAsyncScope();
      return await scope
        .ServiceProvider.GetRequiredService<ISourceRoadStore>()
        .ClaimAsync(
          clock.GetUtcNow().UtcDateTime,
          lease ?? TimeSpan.FromMinutes(3),
          default
        );
    }

    public ReadCache Reads => reads;
    public RoutePreparationQueue Queue => queue;
    public Router Router => router;
    public Truck Truck => truck;
    public BaseRouteOperation Operation => operation;

    public static async Task<Fixture> CreateAsync(
      RoutePreparationOptions? configuration = null
    )
    {
      var connection = new SqliteConnection("Data Source=:memory:");
      await connection.OpenAsync();
      var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
      var options = Options.Create(
        configuration ?? new RoutePreparationOptions()
      );
      var queue = new RoutePreparationQueue(options, clock);
      var reads = TestCache.Create();
      var router = new Router();
      var services = new ServiceCollection();
      services.AddSingleton(reads);
      services.AddScoped(_ => new AppDbContext(
        new DbContextOptionsBuilder<AppDbContext>()
          .UseSqlite(connection)
          .Options
      ));
      services.AddScoped<IAppDbContext>(provider =>
        provider.GetRequiredService<AppDbContext>()
      );
      services.AddScoped(provider => new PlanningTestServices(
        provider.GetRequiredService<IAppDbContext>(),
        router,
        sender: new DispatchTelemetrySender(new()),
        reads: reads
      ));
      services.AddScoped(provider =>
        provider.GetRequiredService<PlanningTestServices>().Routes
      );
      services.AddScoped(provider =>
        provider.GetRequiredService<PlanningTestServices>().Deadheads
      );
      services.AddScoped(provider =>
        provider.GetRequiredService<PlanningTestServices>().BaseRoutes
      );
      services.AddScoped<ISourceRoadStore, SourceRoadStore>();
      services.AddScoped(provider => new SourceRoadInputs(
        provider.GetRequiredService<IAppDbContext>(),
        provider.GetRequiredService<PlanningTestServices>().Profiles,
        provider.GetRequiredService<PlanningTestServices>().DeadheadHistory,
        new ExecutionReadScope(provider.GetRequiredService<AppDbContext>())
      ));
      services.AddScoped(provider => new StopAddressService(
        provider.GetRequiredService<IAppDbContext>(),
        new NoAddressLookup(),
        reads,
        queue
      ));
      services.AddScoped(provider => new ExecutionStopAddressService(
        provider.GetRequiredService<IAppDbContext>(),
        new NoAddressLookup(),
        reads,
        queue,
        clock
      ));
      var root = services.BuildServiceProvider(
        new ServiceProviderOptions { ValidateScopes = true }
      );
      var truck = new Truck
      {
        Id = Guid.NewGuid(),
        ExternalId = "preparation",
        UnitNumber = "Preparation",
        IsActive = true,
      };
      await using (var scope = root.CreateAsyncScope())
      {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();
        db.Trucks.Add(truck);
        await db.SaveChangesAsync();
      }
      var operation = new BaseRouteOperation(
        root.GetRequiredService<IServiceScopeFactory>(),
        NullLogger<BaseRouteOperation>.Instance,
        queue,
        options,
        clock
      );
      return new(
        connection,
        root,
        reads,
        queue,
        router,
        clock,
        truck,
        operation
      );
    }

    public async Task<Guid> AddAsync(
      int days,
      string status = "assigned",
      bool stopOnly = false
    )
    {
      await using var scope = root.CreateAsyncScope();
      var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
      var date = DateOnly
        .FromDateTime(clock.GetUtcNow().UtcDateTime)
        .AddDays(days);
      var load = new Dispatch
      {
        Id = Guid.NewGuid(),
        LoadNumber = ++loadNumber,
        TruckId = stopOnly ? null : truck.Id,
        Status = status,
        ShipDate = date,
        DeliveryDate = date,
        Price = 100,
        Currency = "USD",
        LoadedMiles = 100,
        Stops =
        [
          new()
          {
            Id = Guid.NewGuid(),
            TruckId = truck.Id,
            Sequence = 1,
            Job = "Pick Up",
            ScheduledDate = date,
            Latitude = 40,
            Longitude = -80 + days / 10m,
          },
          new()
          {
            Id = Guid.NewGuid(),
            TruckId = truck.Id,
            Sequence = 2,
            Job = "Drop Off",
            ScheduledDate = date,
            Latitude = 41,
            Longitude = -79 + days / 10m,
          },
        ],
      };
      db.Dispatches.Add(load);
      await db.SaveChangesAsync();
      return load.Id;
    }

    public async Task<(
      Guid,
      ExecutionLeg[],
      SwitchParticipant
    )> AddTransferAsync(bool archived = false)
    {
      var id = await AddAsync(0, "in_transit");
      await using var scope = root.CreateAsyncScope();
      var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
      var load = await db
        .Dispatches.Include(x => x.Stops)
        .SingleAsync(x => x.Id == id);
      if (archived)
        load.Status = "completed";
      var incomingTruck = new Truck
      {
        Id = Guid.NewGuid(),
        UnitNumber = "54777",
        ExternalId = "54777",
      };
      db.Trucks.Add(incomingTruck);
      var trips = new[]
      {
        new Trip { Id = Guid.NewGuid() },
        new Trip { Id = Guid.NewGuid() },
      };
      db.Trips.AddRange(trips);
      var transfer = new DispatchSwitchOperation
      {
        Id = Guid.NewGuid(),
        Status = "completed",
        Revision = 2,
        SiteName = "Named transfer point",
        Latitude = 40.5m,
        Longitude = -79.5m,
        RecordedBy = Guid.NewGuid(),
        RecordedAt = DateTime.UtcNow,
        IdempotencyKey = Guid.NewGuid(),
      };
      db.DispatchSwitchOperations.Add(transfer);
      var visits = trips
        .Select(
          (trip, index) =>
            new ExecutionTransferVisit
            {
              Id = Guid.NewGuid(),
              TripId = trip.Id,
              Operation = index == 0 ? "Drop" : "Hook",
              SiteName = transfer.SiteName,
              Latitude = transfer.Latitude,
              Longitude = transfer.Longitude,
              ActualAt = DateTime.UtcNow.Date.AddDays(index - 1),
              ConfirmedBy = transfer.RecordedBy,
              Revision = 1,
            }
        )
        .ToArray();
      var original = load.Stops.OrderBy(x => x.Sequence).ToArray();
      var legs = trips
        .Select(
          (trip, index) =>
            new ExecutionLeg
            {
              Id = Guid.NewGuid(),
              TripId = trip.Id,
              TruckId = index == 0 ? truck.Id : incomingTruck.Id,
              Status = archived || index == 0 ? "completed" : "active",
              Revision = 2,
              EndSwitchId = index == 0 ? transfer.Id : null,
              StartSwitchId = index == 1 ? transfer.Id : null,
              Stops = ExecutionStopRows.Capture(
                index == 0
                  ? new[]
                  {
                    original[0],
                    ExecutionSnapshots.Boundary(visits[0], id, 2, "Loaded"),
                  }
                  : new[]
                  {
                    ExecutionSnapshots.Boundary(visits[1], id, 0, "Loaded"),
                    original[1],
                  }
              ),
            }
        )
        .ToArray();
      db.ExecutionLegs.AddRange(legs);
      db.LoadExecutionLegs.AddRange(
        legs.Select(
          (leg, index) =>
            new LoadExecutionLeg
            {
              Id = Guid.NewGuid(),
              DispatchId = id,
              ExecutionLegId = leg.Id,
              Sequence = index,
            }
        )
      );
      var participant = new SwitchParticipant
      {
        Id = Guid.NewGuid(),
        SwitchId = transfer.Id,
        DispatchId = id,
        OutgoingLegId = legs[0].Id,
        IncomingLegId = legs[1].Id,
        ReleaseVisitId = visits[0].Id,
        ReceiveVisitId = visits[1].Id,
        ReleasedAt = visits[0].ActualAt,
        ReceivedAt = visits[1].ActualAt,
        ReleasedBy = transfer.RecordedBy,
        ReceivedBy = transfer.RecordedBy,
        Revision = 3,
      };
      db.SwitchParticipants.Add(participant);
      db.DispatchRoutePlans.Add(
        new DispatchRoutePlan
        {
          Id = Guid.NewGuid(),
          DispatchId = id,
          ExecutionLegId = legs[1].Id,
          TruckId = incomingTruck.Id,
          AssignmentRevision = 2,
          PlanJson = "{\"fromCurrentPosition\":true,\"stops\":[]}",
        }
      );
      await db.SaveChangesAsync();
      return (id, legs, participant);
    }

    public async ValueTask DisposeAsync()
    {
      await root.DisposeAsync();
      await connection.DisposeAsync();
    }
  }

  private sealed class Router : IRoutingProvider
  {
    public bool IsConfigured => true;
    public int Calls { get; private set; }
    public int GeocodeCalls { get; private set; }
    public List<double> Heights { get; } = [];
    public Func<
      TruckRouteProfile,
      CancellationToken,
      Task
    >? BeforeCalculate { get; set; }

    public async Task<TruckRoute> CalculateAsync(
      IReadOnlyList<RoutePoint> points,
      TruckRouteProfile profile,
      CancellationToken ct
    )
    {
      Calls++;
      Heights.Add(profile.HeightFeet);
      if (BeforeCalculate is { } before)
        await before(profile, ct);
      return new()
      {
        Miles = 100,
        Seconds = 100,
        Points = points.ToList(),
        Legs = points
          .Zip(
            points.Skip(1),
            (from, to) =>
              new RouteLeg(
                100 / (points.Count - 1d),
                100 / (points.Count - 1d),
                [from, to]
              )
          )
          .ToList(),
      };
    }

    public Task<RoutePoint> GeocodeAsync(string address, CancellationToken ct)
    {
      GeocodeCalls++;
      throw new InvalidOperationException(
        "No imported address lookup expected."
      );
    }
  }

  private sealed class NoAddressLookup : IAddressGeocoder
  {
    public Task<RoutePoint> GeocodeAsync(
      string address,
      CancellationToken ct
    ) => throw new InvalidOperationException("No address lookup expected.");

    public Task<ResolvedAddress> ResolveAsync(
      string address,
      CancellationToken ct
    ) => throw new InvalidOperationException("No address lookup expected.");
  }
}
