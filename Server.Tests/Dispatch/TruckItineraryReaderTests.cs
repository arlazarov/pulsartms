using Application.Features.Execution.Models;
using Application.Features.Execution.Queries;
using Application.Features.Execution.Services;
using Application.Reference;
using Domain.Entities.Execution;
using Domain.Entities.Fleet;
using Infrastructure.Persistence;
using Server.Tests.Support;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Integration")]
public sealed class TruckItineraryReaderTests
{
  private static readonly DateTimeOffset AsOf = new(
    2026,
    9,
    16,
    12,
    0,
    0,
    TimeSpan.Zero
  );

  [Fact]
  public async Task OverdueAndPlannedAssignmentsRemainInTheCompleteRead()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var truck = await AssignAsync(f);
    f.Load.ShipDate = f.Load.DeliveryDate = new(2026, 9, 14);
    var planned = Assigned(truck, "planned", 2);
    var completed = Assigned(truck, "assigned", 3);
    completed.Stops[^1].DeliveredAt = AsOf.UtcDateTime;
    var cancelled = Assigned(truck, "cancelled", 4);
    f.Db.Dispatches.AddRange(planned, completed, cancelled);
    await f.Db.SaveChangesAsync();

    var snapshot = await ReadAsync(f, truck);

    Assert.Equal(2, snapshot.Segments.Length);
    Assert.True(
      snapshot.Segments.Single(x => x.Work.DispatchId == f.Load.Id).IsOverdue
    );
    Assert.Contains(snapshot.Segments, x => x.Work.DispatchId == planned.Id);
    Assert.Equal(0, f.Planning.Hos.ClockCalls);
    Assert.False(f.Db.ChangeTracker.HasChanges());
  }

  [Fact]
  public async Task AnUnresolvedTruckPathRetainsEveryAcceptedVisit()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var truck = await AssignAsync(f);
    f.Load.Stops[1].ManualAction = "Driver start";
    f.Load.Stops[1].ManualStateAfter = "No truck";
    await f.Db.SaveChangesAsync();

    var segment = Assert.Single((await ReadAsync(f, truck)).Segments);

    Assert.Contains(WorkReadProblem.UnresolvedTruckPath, segment.Problems);
    Assert.Equal(5, segment.Visits.Length);
    Assert.All(segment.Visits, x => Assert.False(x.InTruckPath));
    Assert.Equal("No truck", segment.Visits[1].StateAfter);
    Assert.Equal(5, segment.Visits.Select(x => x.Id).Distinct().Count());
  }

  [Fact]
  public async Task AConfirmedStartMarksEarlierVisitsWithoutDeletingThem()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var truck = await AssignAsync(f);
    f.Load.PlanningTruckId = truck.Id;
    f.Load.PlanningFromStopId = f.Load.Stops[2].Id;
    f.Load.PlanningAssignmentRevision = 7;
    await f.Db.SaveChangesAsync();

    var segment = Assert.Single((await ReadAsync(f, truck)).Segments);

    Assert.Empty(segment.Problems);
    Assert.Equal(7, segment.AssignmentRevision);
    Assert.Equal(
      new[] { false, false, true, true, true },
      segment.Visits.Select(x => x.InTruckPath)
    );
    Assert.Equal(
      f.Load.Stops.Select(x => x.Id),
      segment.Visits.Select(x => x.SourceStopId!.Value)
    );
  }

  [Fact]
  public async Task ConflictingStopAssignmentsRemainVisibleAsProblems()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var truck = await AssignAsync(f);
    var other = new Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = "other",
      UnitNumber = "Other",
    };
    f.Db.Trucks.Add(other);
    f.Load.Stops[2].TruckId = other.Id;
    await f.Db.SaveChangesAsync();

    var segment = Assert.Single((await ReadAsync(f, truck)).Segments);

    Assert.Contains(WorkReadProblem.ConflictingAssignment, segment.Problems);
    Assert.Equal(other.Id, segment.Visits[2].TruckId);
  }

  [Fact]
  public async Task NativeSuccessorsAndCompletedBoundaryReferencesAreRetained()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var truck = await AssignAsync(f);
    var past = AddLeg(f, truck, 1, "completed");
    var current = AddLeg(f, truck, 2, "active");
    var future = AddLeg(f, truck, 3, "planned");
    await f.Db.SaveChangesAsync();

    var snapshot = await ReadAsync(f, truck);

    Assert.Equal(
      new[] { current.Id, future.Id },
      snapshot.Segments.Select(x => x.Work.ExecutionLegId!.Value)
    );
    Assert.DoesNotContain(
      snapshot.Segments,
      x => x.Work.ExecutionLegId == past.Id
    );
    var boundary = snapshot.Evidence.Legs.Single(x =>
      x.ExecutionLegId == past.Id
    );
    Assert.Equal("completed", boundary.Status);
    Assert.Equal(truck.Id, boundary.TruckId);
    Assert.Equal(f.Load.Stops[0].Id, boundary.StartVisitId);
    Assert.Equal(f.Load.Stops[^1].Id, boundary.EndVisitId);
    Assert.Equal(
      WorkPrecedenceBasis.ExecutionLink,
      Assert.Single(snapshot.Sequence.Precedence).Basis
    );
  }

  [Fact]
  public async Task MissingNativeVisitsCannotHideTheirAssignment()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var truck = await AssignAsync(f);
    var leg = AddLeg(f, truck, 1, "active");
    leg.Stops.Clear();
    await f.Db.SaveChangesAsync();

    var segment = Assert.Single((await ReadAsync(f, truck)).Segments);

    Assert.Equal(leg.Id, segment.Work.ExecutionLegId);
    Assert.Empty(segment.Visits);
    Assert.Contains(WorkReadProblem.MissingVisits, segment.Problems);
  }

  [Fact]
  public async Task ConfirmedUnknownTimesAndSourceReviewSurviveProjection()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var truck = await AssignAsync(f);
    var leg = AddLeg(f, truck, 1, "active");
    leg.SourceSignature = "accepted";
    leg.SourceObservedSignature = "observed";
    leg.SourceReviewReason = "Appointment changed.";
    var former = AddLeg(f, truck, 0, "completed");
    var operation = new DispatchSwitchOperation
    {
      Id = Guid.NewGuid(),
      IdempotencyKey = Guid.NewGuid(),
    };
    leg.StartSwitchId = operation.Id;
    var received = leg.Stops.OrderBy(x => x.Position).First();
    received.SourceDispatchStopId = f.Load.Stops[0].Id;
    f.Db.SwitchParticipants.Add(
      new()
      {
        Id = Guid.NewGuid(),
        Switch = operation,
        DispatchId = f.Load.Id,
        OutgoingLegId = former.Id,
        IncomingLegId = leg.Id,
        ReleaseVisitId = Guid.NewGuid(),
        ReceiveVisitId = received.Id,
        ReleasedBy = f.Actor.Id,
        ReceivedBy = f.Actor.Id,
        Revision = 4,
      }
    );
    await f.Db.SaveChangesAsync();

    var segment = Assert.Single((await ReadAsync(f, truck)).Segments);
    var visit = segment.Visits[0];

    Assert.True(visit.Actuals.ExecutionConfirmed);
    Assert.True(visit.Actuals.IsCompleted);
    Assert.Null(visit.Actuals.ConfirmedAt);
    Assert.Equal(f.Actor.Id, visit.Actuals.ConfirmedBy);
    Assert.Equal(4, visit.Actuals.CompletionRevision);
    Assert.Equal(f.Load.Stops[0].Id, visit.SourceStopId);
    Assert.Equal("accepted", segment.AcceptedSourceSignature);
    Assert.Equal("observed", segment.ObservedSourceSignature);
    Assert.Contains(WorkReadProblem.SourceReviewRequired, segment.Problems);
  }

  [Fact]
  public async Task MissingTransferRecordsRemainAnExplicitProblem()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var truck = await AssignAsync(f);
    var leg = AddLeg(f, truck, 1, "active");
    var operation = new DispatchSwitchOperation
    {
      Id = Guid.NewGuid(),
      IdempotencyKey = Guid.NewGuid(),
    };
    f.Db.DispatchSwitchOperations.Add(operation);
    leg.StartSwitchId = operation.Id;
    await f.Db.SaveChangesAsync();

    var segment = Assert.Single((await ReadAsync(f, truck)).Segments);

    Assert.Contains(WorkReadProblem.MissingTransfer, segment.Problems);
  }

  [Fact]
  public async Task SignaturesDetectChangedFactsEvenWithoutARevisionIncrement()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var truck = await AssignAsync(f);
    var reader = Reader(f);
    var first = await ReadAsync(f, truck);
    Assert.True(await reader.MatchesAsync(first, default));
    var later = await reader.ReadAsync(truck.Id, AsOf.AddHours(1), default);
    Assert.Equal(first.InputSignature, later!.InputSignature);
    f.Load.Stops[0].ScheduledDate = new(2026, 9, 20);
    await f.Db.SaveChangesAsync();

    Assert.False(await reader.MatchesAsync(first, default));
    Assert.Null(first.Segments[0].Visits[0].Appointment.Date);
    Assert.Equal(
      new DateOnly(2026, 9, 20),
      (await ReadAsync(f, truck)).Segments[0].Visits[0].Appointment.Date
    );
  }

  [Fact]
  public async Task MembershipAndFleetConfigurationParticipateInTheSignature()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var truck = await AssignAsync(f);
    var reader = Reader(f);
    var first = await ReadAsync(f, truck);
    f.Db.Dispatches.Add(Assigned(truck, "assigned", 2));
    await f.Db.SaveChangesAsync();
    Assert.False(await reader.MatchesAsync(first, default));
    var second = await ReadAsync(f, truck);
    truck.ConfigurationRevision++;
    await f.Db.SaveChangesAsync();
    Assert.False(await reader.MatchesAsync(second, default));
  }

  [Fact]
  public async Task StoredReadsNeitherPublishNorDiscardPendingTrackedEdits()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var truck = await AssignAsync(f);
    f.Load.Stops[0].Address = "Unsaved local edit";

    var snapshot = await ReadAsync(f, truck);

    Assert.Equal(
      "1 Main Road",
      snapshot.Segments[0].Visits[0].Location.Address
    );
    Assert.Equal("Unsaved local edit", f.Load.Stops[0].Address);
    Assert.True(f.Db.ChangeTracker.HasChanges());
    Assert.Null(f.Db.Database.CurrentTransaction);
  }

  [Fact]
  public async Task EmptyInactiveTruckDiffersFromAnUnknownTruck()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var truck = new Truck { Id = Guid.NewGuid(), UnitNumber = "Inactive" };
    f.Db.Trucks.Add(truck);
    await f.Db.SaveChangesAsync();
    var reader = Reader(f);

    var empty = await reader.ReadAsync(truck.Id, AsOf, default);

    Assert.NotNull(empty);
    Assert.Empty(empty.Segments);
    Assert.False(empty.Resources.IsActive);
    Assert.Null(await reader.ReadAsync(Guid.NewGuid(), AsOf, default));
  }

  [Fact]
  public async Task RevalidationCannotReuseAnOlderCallerSnapshot()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var truck = await AssignAsync(f);
    var snapshot = await ReadAsync(f, truck);
    await using var transaction = await f.Db.Database.BeginTransactionAsync();

    await Assert.ThrowsAsync<InvalidOperationException>(
      () => Reader(f).MatchesAsync(snapshot, default)
    );

    Assert.Same(transaction, f.Db.Database.CurrentTransaction);
  }

  [Fact]
  public async Task BatchSnapshotsMatchIndividualReadsAndIsolateOtherTrucks()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var first = await AssignAsync(f);
    var second = new Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = "second",
      UnitNumber = "Second",
      IsActive = true,
    };
    f.Db.Trucks.Add(second);
    var secondLoad = Assigned(second, "assigned", 2);
    f.Db.Dispatches.Add(secondLoad);
    var leg = AddLeg(f, first, 1, "active");
    await f.Db.SaveChangesAsync();
    var reader = Reader(f);
    var firstRead = await reader.ReadAsync(first.Id, AsOf, default);
    var secondRead = await reader.ReadAsync(second.Id, AsOf, default);
    var batch = await reader.ReadManyAsync(
      [first.Id, second.Id, first.Id, Guid.NewGuid()],
      AsOf,
      default
    );
    Assert.Equal(2, batch.Count);
    Assert.Equal(firstRead!.InputSignature, batch[first.Id].InputSignature);
    Assert.Equal(secondRead!.InputSignature, batch[second.Id].InputSignature);
    Assert.Equal(
      leg.Id,
      Assert.Single(batch[first.Id].Segments).Work.ExecutionLegId
    );
    Assert.Equal(
      secondLoad.Id,
      Assert.Single(batch[second.Id].Segments).Work.DispatchId
    );
    Assert.Empty(batch[second.Id].Evidence.Legs);
    var narrow = await reader.ReadManyAsync([second.Id], AsOf, default);
    Assert.Equal(second.Id, Assert.Single(narrow).Key);
  }

  private static TruckItineraryReader Reader(StopCompletionFixture f) =>
    new(
      f.Db,
      new ExecutionReadScope(f.Db),
      new FleetNames(f.Db),
      new ActiveTransfers(f.Db)
    );

  private static async Task<TruckItinerarySnapshot> ReadAsync(
    StopCompletionFixture f,
    Truck truck
  ) =>
    Assert.IsType<TruckItinerarySnapshot>(
      await new GetTruckItineraryHandler(Reader(f)).Handle(
        new(truck.Id, AsOf),
        default
      )
    );

  private static async Task<Truck> AssignAsync(StopCompletionFixture f)
  {
    var truck = new Truck
    {
      Id = Guid.NewGuid(),
      UnitNumber = "Complete",
      IsActive = true,
    };
    f.Db.Trucks.Add(truck);
    f.Load.TruckId = truck.Id;
    await f.Db.SaveChangesAsync();
    return truck;
  }

  private static Load Assigned(Truck truck, string status, int number) =>
    new()
    {
      Id = Guid.NewGuid(),
      TruckId = truck.Id,
      Status = status,
      LoadNumber = number,
      Stops =
      [
        new()
        {
          Id = Guid.NewGuid(),
          Sequence = 1,
          Job = "Pick Up",
        },
        new()
        {
          Id = Guid.NewGuid(),
          Sequence = 2,
          Job = "Drop Off",
        },
      ],
    };

  private static ExecutionLeg AddLeg(
    StopCompletionFixture f,
    Truck truck,
    int sequence,
    string status
  )
  {
    var leg = new ExecutionLeg
    {
      Id = Guid.NewGuid(),
      Trip = new() { Id = Guid.NewGuid() },
      TruckId = truck.Id,
      Status = status,
      Revision = sequence,
      Stops = ExecutionStopRows.Capture(f.Load.Stops),
    };
    f.Db.LoadExecutionLegs.Add(
      new()
      {
        Id = Guid.NewGuid(),
        DispatchId = f.Load.Id,
        ExecutionLeg = leg,
        Sequence = sequence,
        StartVisitId = f.Load.Stops[0].Id,
        EndVisitId = f.Load.Stops[^1].Id,
      }
    );
    return leg;
  }
}
