using Application.Features.Dispatch.Models;
using Application.Features.Execution.Models;
using Application.Features.Execution.Queries;
using Application.Features.Execution.Services;
using Application.Features.Routing.Interfaces;
using Application.Reference;
using Domain.Entities.Dispatch;
using Domain.Entities.Execution;
using Domain.Entities.Fleet;
using Domain.Entities.Fuel;
using Domain.Models.Execution;
using Domain.Models.Routing;
using Domain.Rules;
using Microsoft.EntityFrameworkCore;
using Server.Tests.Support;

namespace Server.Tests.Dispatch;

[Trait("Category", "Synchronization")]
[Trait("Kind", "Integration")]
public sealed class ExecutionItineraryReadTests
{
  [Theory]
  [Trait("Category", "Fuel")]
  [InlineData("build", false)]
  [InlineData("build", true)]
  [InlineData("edit", false)]
  [InlineData("edit", true)]
  [InlineData("preview", false)]
  [InlineData("preview", true)]
  [InlineData("reset", false)]
  [InlineData("reset", true)]
  public async Task FuelChangesRejectMissingOrStaleExecutionBeforeCalculation(
    string operation,
    bool stale
  )
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var state = await SeedAsync(f);
    var router = new ConfirmedRouteProvider();
    using var planning = new PlanningTestServices(f.Db, router);
    Guid? legId = stale ? state.Incoming.Id : null;
    long? revision = stale ? state.Incoming.Revision - 1 : null;

    await Assert.ThrowsAsync<PlanningSettingsConflictException>(async () =>
    {
      switch (operation)
      {
        case "build":
          await planning.Fuel.BuildAsync(
            f.Load.Id,
            new(new() { Confirmed = true })
            {
              ExecutionLegId = legId,
              AssignmentRevision = revision,
            },
            default
          );
          break;
        case "preview":
        case "edit":
          await planning.Fuel.EditAsync(
            f.Load.Id,
            new(null, [])
            {
              ExecutionLegId = legId,
              AssignmentRevision = revision,
            },
            operation == "edit",
            default
          );
          break;
        case "reset":
          await planning.Fuel.ResetAsync(
            f.Load.Id,
            null,
            default,
            legId,
            revision
          );
          break;
      }
    });

    Assert.Equal(0, router.GeocodeCalls);
    Assert.Equal(0, router.RouteCalls);
    Assert.Equal(0, planning.Hos.ClockCalls);
    Assert.Empty(await f.Db.Set<TruckFuelPlan>().ToListAsync());
  }

  [Fact]
  public async Task ActiveNativeRouteBuildNeverGeocodesDuplicateSourceTransfers()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var state = await SeedAsync(f);
    var router = new ConfirmedRouteProvider();
    using var planning = new PlanningTestServices(f.Db, router);
    var plan = await planning.Routes.BuildAsync(
      f.Load.Id,
      new(new() { Confirmed = true }, ExecutionLegId: state.Incoming.Id),
      default
    );

    Assert.Equal(0, router.GeocodeCalls);
    Assert.Equal(1, router.RouteCalls);
    Assert.Equal(state.Incoming.Id, plan.ExecutionLegId);
    Assert.Equal(7, plan.AssignmentRevision);
    Assert.Equal(
      new[] { state.Hook.Id, f.Load.Stops[3].Id },
      plan.Stops.Select(x => x.Id)
    );
    Assert.Equal(50, plan.Route.Miles);
  }

  [Fact]
  public async Task ImportedTransferCopiesNeverReenterActiveTruckRoute()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var state = await SeedAsync(f);
    var expected = new[] { state.Hook.Id, f.Load.Stops[3].Id };
    var itinerary = await new GetExecutionItineraryHandler(f.Db).Handle(
      new(f.Load.Id, state.Incoming.Id, state.Truck.Id),
      default
    );
    var board = await new GetTruckExecutionLoadsHandler(
      f.Db,
      new FleetNames(f.Db),
      new ActiveTransfers(f.Db)
    ).Handle(new(state.Truck.Id, [f.Load.Id]), default);

    Assert.NotNull(itinerary);
    Assert.Equal(expected, itinerary.Stops.Select(x => x.Id));
    Assert.True(itinerary.Stops[0].IsCompleted);
    var load = Assert.Single(board.Loads).Work;
    Assert.Equal(expected, load.Stops.Select(x => x.Id));
    Assert.Equal(7, load.AssignmentRevision);
    Assert.Equal(7, itinerary.Leg.Revision);
    Assert.Equal("Hook", load.Stops[0].Job);
    Assert.True(load.Stops[0].IsCompleted);
    Assert.Contains(f.Load.Id, board.OwnedDispatchIds);
    Assert.Equal(4, f.Load.Stops.Count);
  }

  [Fact]
  public async Task NewFutureStopIsCommittedWithRevisionAndRebuildOnce()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var state = await SeedAsync(f);
    var added = new DispatchStop
    {
      Id = Guid.NewGuid(),
      DispatchId = f.Load.Id,
      Sequence = 5,
      Job = "Drop Off",
      Address = "500 Main Street",
      TruckId = state.Truck.Id,
    };
    f.Load.Stops.Add(added);
    f.Db.DispatchStops.Add(added);
    await f.Db.SaveChangesAsync();
    var before = await new GetExecutionItineraryHandler(f.Db).Handle(
      new(f.Load.Id, state.Incoming.Id),
      default
    );
    Assert.Equal(2, before!.Stops.Count);
    var outgoing = ExecutionSnapshots.Write(
      ExecutionStopRows.Read(state.Outgoing)
    );
    await using var transaction = await f.Db.Database.BeginTransactionAsync();
    var changed = await ExecutionSourceReconciliation.ApplyAsync(
      f.Db,
      [f.Load],
      DateTime.UtcNow,
      default
    );
    await f.Db.SaveChangesAsync();

    Assert.Equal(state.Incoming.Id, Assert.Single(changed).Id);
    Assert.Equal(8, state.Incoming.Revision);
    Assert.Null(state.Incoming.SourceReviewReason);
    Assert.Equal(
      outgoing,
      ExecutionSnapshots.Write(ExecutionStopRows.Read(state.Outgoing))
    );
    Assert.Equal(3, state.Outgoing.Revision);
    var stops = ExecutionStopRows.Read(state.Incoming);
    Assert.Equal(
      new[] { state.Hook.Id, f.Load.Stops[3].Id, added.Id },
      stops.Select(x => x.Id)
    );
    var link = Assert.Single(state.Incoming.Loads);
    Assert.Equal(state.Hook.Id, link.StartVisitId);
    Assert.Equal(added.Id, link.EndVisitId);
    var work = Assert.Single(await f.Db.ExecutionPlanningChanges.ToListAsync());
    Assert.Equal(state.Incoming.Id, work.ExecutionLegId);
    Assert.Equal(8, work.AssignmentRevision);
    Assert.Equal(state.Truck.Id, work.TruckId);

    var repeated = await ExecutionSourceReconciliation.ApplyAsync(
      f.Db,
      [f.Load],
      DateTime.UtcNow,
      default
    );
    Assert.Empty(repeated);
    Assert.Equal(8, state.Incoming.Revision);
    Assert.Single(await f.Db.ExecutionPlanningChanges.ToListAsync());
    var version = await f.Db.ExecutionLegRevisions.SingleAsync();
    Assert.Equal("source-synchronized", version.Operation);
    Assert.Null(version.RecordedBy);
    Assert.Equal(8, version.Revision);
    Assert.Equal(
      stops.Select(x => x.Id),
      ExecutionRevisionFacts.Read(version).Stops.Select(x => x.Id)
    );
    await transaction.CommitAsync();

    var board = await new GetTruckExecutionLoadsHandler(
      f.Db,
      new FleetNames(f.Db),
      new ActiveTransfers(f.Db)
    ).Handle(new(state.Truck.Id, []), default);
    var current = Assert.Single(board.Loads).Work;
    Assert.Equal(8, current.AssignmentRevision);
    Assert.Equal(stops.Select(x => x.Id), current.Stops.Select(x => x.Id));
  }

  [Fact]
  public async Task RescheduledDeliveryAfterTransferRebuildsOnlyActiveLegOnce()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var state = await SeedAsync(f);
    var outgoing = ExecutionSnapshots.Write(
      ExecutionStopRows.Read(state.Outgoing)
    );
    var source = f.Load.Stops[3];
    source.ScheduledDate = new(2026, 9, 16);
    source.ScheduledTime = new(9, 0);
    source.ArrivedAt = DateTime.UtcNow.AddMinutes(-10);
    state.Incoming.SourceReviewReason = "Source appointment needs review.";
    await using var transaction = await f.Db.Database.BeginTransactionAsync();
    var changed = await ExecutionSourceReconciliation.ApplyAsync(
      f.Db,
      [f.Load],
      DateTime.UtcNow,
      default
    );
    await f.Db.SaveChangesAsync();
    Assert.Equal(state.Incoming.Id, Assert.Single(changed).Id);
    Assert.Equal(8, state.Incoming.Revision);
    Assert.Null(state.Incoming.SourceReviewReason);
    Assert.Equal("active", state.Incoming.Status);
    Assert.Equal(
      outgoing,
      ExecutionSnapshots.Write(ExecutionStopRows.Read(state.Outgoing))
    );
    Assert.Equal(3, state.Outgoing.Revision);
    var stops = ExecutionStopRows.Read(state.Incoming);
    Assert.Equal(state.Hook.Id, stops[0].Id);
    Assert.Equal(source.Id, stops[1].Id);
    Assert.Equal(StopAppointment.From(source), StopAppointment.From(stops[1]));
    Assert.Equal(source.ArrivedAt, stops[1].ArrivedAt);
    var work = Assert.Single(await f.Db.ExecutionPlanningChanges.ToListAsync());
    Assert.Equal(state.Incoming.Id, work.ExecutionLegId);
    Assert.Equal(8, work.AssignmentRevision);
    Assert.Empty(
      await ExecutionSourceReconciliation.ApplyAsync(
        f.Db,
        [f.Load],
        DateTime.UtcNow,
        default
      )
    );
    Assert.Equal(8, state.Incoming.Revision);
    Assert.Single(await f.Db.ExecutionPlanningChanges.ToListAsync());
    await transaction.CommitAsync();
  }

  private static async Task<SeededExecution> SeedAsync(StopCompletionFixture f)
  {
    f.Db.DispatchStops.Remove(f.Load.Stops[4]);
    f.Load.Stops.RemoveAt(4);
    var truck = new Truck
    {
      Id = Guid.NewGuid(),
      UnitNumber = "11005",
      ExternalId = "execution-read-truck",
    };
    var former = new Truck
    {
      Id = Guid.NewGuid(),
      UnitNumber = "54777",
      ExternalId = "execution-read-former-truck",
    };
    foreach (var source in f.Load.Stops)
    {
      source.TruckId = truck.Id;
      source.TruckNumber = truck.UnitNumber;
    }
    f.Load.Stops[1].Job = "Drop";
    f.Load.Stops[2].Job = "Hook";
    f.Load.Stops[3].Job = "Drop Off";
    foreach (var source in f.Load.Stops.Skip(1).Take(2))
    {
      source.Address = "145 Major Grahams Road";
      source.City = "MAX MEADOWS";
      source.Province = "VA";
      source.Country = "USA";
      source.ZipCode = "24360";
    }
    foreach (var source in new[] { f.Load.Stops[0], f.Load.Stops[3] })
    {
      source.Latitude = 27.22m;
      source.Longitude = -80.41m;
      source.AddressVerifiedAt = DateTime.UtcNow;
      source.SourceAddressJson = StopAddress.From(source).Serialize();
    }
    var trip = new Trip { Id = Guid.NewGuid() };
    var operation = new DispatchSwitchOperation
    {
      Id = Guid.NewGuid(),
      IdempotencyKey = Guid.NewGuid(),
      Status = "completed",
      RecordedBy = f.Actor.Id,
    };
    var drop = Visit(f.Load.Stops[1], "Drop");
    var hook = Visit(f.Load.Stops[2], "Hook");
    var outgoing = new ExecutionLeg
    {
      Id = Guid.NewGuid(),
      TripId = trip.Id,
      TruckId = former.Id,
      Status = "completed",
      Revision = 3,
      EndSwitchId = operation.Id,
      Stops = ExecutionStopRows.Capture(
        [
          f.Load.Stops[0],
          ExecutionSnapshots.Boundary(drop, f.Load.Id, 1, "Loaded"),
        ]
      ),
    };
    var incoming = new ExecutionLeg
    {
      Id = Guid.NewGuid(),
      TripId = trip.Id,
      TruckId = truck.Id,
      Status = "active",
      Revision = 7,
      StartSwitchId = operation.Id,
      Stops = ExecutionStopRows.Capture(
        [
          ExecutionSnapshots.Boundary(hook, f.Load.Id, 0, "Loaded"),
          f.Load.Stops[3],
        ]
      ),
    };
    var participant = new SwitchParticipant
    {
      Id = Guid.NewGuid(),
      SwitchId = operation.Id,
      DispatchId = f.Load.Id,
      OutgoingLegId = outgoing.Id,
      IncomingLegId = incoming.Id,
      ReleaseVisitId = drop.Id,
      ReceiveVisitId = hook.Id,
      ReleasedBy = f.Actor.Id,
      ReceivedBy = f.Actor.Id,
    };
    outgoing.Stops.Single(x => x.Id == drop.Id).SourceDispatchStopId =
      drop.SourceDispatchStopId;
    incoming.Stops.Single(x => x.Id == hook.Id).SourceDispatchStopId =
      hook.SourceDispatchStopId;
    outgoing.Loads.Add(Link(outgoing, 1));
    incoming.Loads.Add(Link(incoming, 2));
    f.Db.AddRange(
      truck,
      former,
      trip,
      operation,
      outgoing,
      incoming,
      participant
    );
    await f.Db.SaveChangesAsync();
    return new(truck, outgoing, incoming, hook);

    ExecutionTransferVisit Visit(DispatchStop source, string job) =>
      new()
      {
        Id = Guid.NewGuid(),
        TripId = trip.Id,
        SourceDispatchStopId = source.Id,
        Operation = job,
        SiteName = "145 Major Grahams Road, Max Meadows, VA",
        Latitude = 36.97m,
        Longitude = -80.91m,
        ConfirmedBy = f.Actor.Id,
        Revision = 1,
      };

    LoadExecutionLeg Link(ExecutionLeg leg, int sequence)
    {
      var stops = ExecutionStopRows.Read(leg);
      return new()
      {
        Id = Guid.NewGuid(),
        DispatchId = f.Load.Id,
        ExecutionLegId = leg.Id,
        Sequence = sequence,
        StartVisitId = stops[0].Id,
        EndVisitId = stops[^1].Id,
      };
    }
  }

  private sealed record SeededExecution(
    Truck Truck,
    ExecutionLeg Outgoing,
    ExecutionLeg Incoming,
    ExecutionTransferVisit Hook
  );

  private sealed class ConfirmedRouteProvider : IRoutingProvider
  {
    public bool IsConfigured => true;
    public int GeocodeCalls { get; private set; }
    public int RouteCalls { get; private set; }

    public Task<RoutePoint> GeocodeAsync(string address, CancellationToken ct)
    {
      GeocodeCalls++;
      throw new RoutePlanningException(
        "The address correction needs confirmation of the street and building "
          + "number; no city-center fallback was used."
      );
    }

    public Task<TruckRoute> CalculateAsync(
      IReadOnlyList<RoutePoint> points,
      TruckRouteProfile profile,
      CancellationToken ct
    )
    {
      RouteCalls++;
      Assert.Equal(2, points.Count);
      return Task.FromResult(
        new TruckRoute
        {
          Miles = 50,
          Seconds = 3600,
          Points = points.ToList(),
          Legs = [new(50, 3600, points.ToList())],
        }
      );
    }
  }
}
