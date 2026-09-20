using Application.Features.Dispatch.Models;
using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services.FuelPlanning;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Unit")]
public sealed class FuelExecutionScopeTests
{
  [Theory]
  [InlineData("active", false, true)]
  [InlineData("planned", false, true)]
  [InlineData("planned", true, false)]
  [InlineData("completed", false, false)]
  public void NativeFuelCalculationDoesNotRequireActivation(
    string status,
    bool awaitingReceipt,
    bool allowed
  )
  {
    var plan = Plan();
    var current = Load(plan);
    current.ExecutionStatus = status;
    current.AwaitingReceipt = awaitingReceipt;
    if (allowed)
      Assert.Same(
        current,
        Assert.Single(FuelHorizon.SelectLoads(plan, [current]))
      );
    else
      Assert.Throws<RoutePlanningException>(
        () => FuelHorizon.SelectLoads(plan, [current])
      );
  }

  [Fact]
  public void NativeHorizonSelectsOnlyOpenedActiveLeg()
  {
    var plan = Plan();
    var current = Load(plan);
    var future = Load(plan);
    future.ExecutionLegId = Guid.NewGuid();
    future.ExecutionStatus = "planned";
    future.AwaitingReceipt = true;
    var chosen = FuelHorizon.SelectLoads(plan, [current, future]);
    Assert.Same(current, Assert.Single(chosen));
    current.AssignmentRevision++;
    Assert.Throws<RoutePlanningException>(
      () => FuelHorizon.SelectLoads(plan, [current, future])
    );
  }

  [Fact]
  public void LegacyHorizonCannotCrossNativeOrDuplicateCommercialScope()
  {
    var plan = Plan();
    var native = Load(plan);
    plan.ExecutionLegId = null;
    plan.AssignmentRevision = 0;
    var legacy = Load(plan);
    Assert.Throws<RoutePlanningException>(
      () => FuelHorizon.SelectLoads(plan, [legacy, native])
    );
    Assert.Throws<RoutePlanningException>(
      () => FuelHorizon.SelectLoads(plan, [legacy, legacy])
    );
    Assert.Same(legacy, Assert.Single(FuelHorizon.SelectLoads(plan, [legacy])));
  }

  [Fact]
  public void AssignmentSignatureChangesForSameLoadDifferentLegOrRevision()
  {
    var load = Load(Plan());
    var initial = FuelHorizon.LoadSignature(load);
    load.AssignmentRevision++;
    Assert.NotEqual(initial, FuelHorizon.LoadSignature(load));
    var revised = FuelHorizon.LoadSignature(load);
    load.ExecutionLegId = Guid.NewGuid();
    Assert.NotEqual(revised, FuelHorizon.LoadSignature(load));
  }

  [Fact]
  public void NativeSavedAssignmentDoesNotCollapseSameLoadFutureLeg()
  {
    var plan = Plan();
    var current = Load(plan);
    var next = Load(plan);
    next.ExecutionLegId = Guid.NewGuid();
    next.ExecutionStatus = "planned";
    var saved = Snapshot(plan);
    saved.Plan.DispatchSignatures[plan.DispatchId] = FuelHorizon.LoadSignature(
      current
    );
    Assert.True(
      FuelPlanProjection.AssignmentsMatch(
        saved.Plan,
        plan.DispatchId,
        [current, next]
      )
    );
    current.AwaitingReceipt = true;
    Assert.False(
      FuelPlanProjection.AssignmentsMatch(
        saved.Plan,
        plan.DispatchId,
        [current, next]
      )
    );
  }

  [Fact]
  public void AnotherLegCannotReuseSavedSnapshotOrExposeItsStations()
  {
    var plan = Plan();
    var saved = Snapshot(plan);
    Assert.True(FuelPlanProjection.SameScope(saved, plan));
    plan.ExecutionLegId = Guid.NewGuid();
    Assert.False(FuelPlanProjection.SameScope(saved, plan));
    var projected = FuelPlanProjection.Project(
      saved,
      new(new(), plan, null, null, null, true),
      [],
      null,
      DateTime.UtcNow
    );
    Assert.True(projected.NeedsRefresh);
    Assert.Equal(plan.ExecutionLegId, projected.ExecutionLegId);
    Assert.Empty(projected.Stops);
  }

  [Fact]
  public void ReceivedExecutionEndingAtDeliveryIncludesFutureLegacyLoads()
  {
    var plan = Plan();
    var current = Delivery(Load(plan));
    var duplicate = Load(plan);
    duplicate.ExecutionLegId = Guid.NewGuid();
    duplicate.ExecutionStatus = "planned";
    duplicate.AwaitingReceipt = true;
    var next = Legacy(plan);
    var later = Legacy(plan);
    var chosen = FuelHorizon.SelectLoads(
      plan,
      [current, duplicate, next, later]
    );
    Assert.Equal(new[] { current, next, later }, chosen);
  }

  [Theory]
  [InlineData(false, false)]
  [InlineData(false, true)]
  [InlineData(true, false)]
  [InlineData(true, true)]
  public void RootTruckAndRevisionMustStillMatch(bool native, bool revision)
  {
    var plan = Plan();
    if (!native)
    {
      plan.ExecutionLegId = null;
      plan.AssignmentRevision = 0;
    }
    var current = Delivery(Load(plan));
    if (revision)
      current.AssignmentRevision++;
    else
      current.TruckId = Guid.NewGuid();
    Assert.Throws<RoutePlanningException>(
      () => FuelHorizon.SelectLoads(plan, [current])
    );
  }

  [Theory]
  [InlineData("Drop")]
  [InlineData("Drop trailer")]
  [InlineData("Release")]
  [InlineData("Switch")]
  public void NativeTransferBoundaryNeverExtendsIntoFutureLoads(string job)
  {
    var plan = Plan();
    var current = Delivery(Load(plan));
    current.Stops[^1].Job = job;
    Assert.Same(
      current,
      Assert.Single(FuelHorizon.SelectLoads(plan, [current, Legacy(plan)]))
    );
  }

  // 11006 stood at its delivery with three more loads accepted for it and
  // two thousand miles to drive, and had nothing to plan fuel for: the chain
  // stops at the first load carrying an execution leg of its own. Chaining
  // them was tried, and the route came out right; the plan could not be
  // kept, because a saved fuel plan is scoped to one leg - the store and the
  // schedule check both require every stop after the root to carry no leg.
  // This pins the boundary as it stands, so that widening the scope is a
  // decision taken deliberately and everywhere at once.
  [Fact]
  public void AnAcceptedFutureLegEndsTheHorizonWhileTheScopeIsOneLeg()
  {
    var plan = Plan();
    var current = Delivery(Load(plan));
    var accepted = Delivery(
      new()
      {
        Id = Guid.NewGuid(),
        TruckId = plan.TruckId,
        ExecutionLegId = Guid.NewGuid(),
        AssignmentRevision = 1,
        ExecutionStatus = "planned",
        Status = "assigned",
      }
    );
    Assert.Same(
      current,
      Assert.Single(
        FuelHorizon.SelectLoads(plan, [current, accepted, Legacy(plan)])
      )
    );
  }

  [Fact]
  public void AnotherNativeAssignmentEndsTheSupportedHorizon()
  {
    var plan = Plan();
    var current = Delivery(Load(plan));
    var boundary = Load(plan);
    boundary.Id = Guid.NewGuid();
    boundary.ExecutionLegId = Guid.NewGuid();
    boundary.ExecutionStatus = "planned";
    boundary.AwaitingReceipt = true;
    Assert.Same(
      current,
      Assert.Single(
        FuelHorizon.SelectLoads(plan, [current, boundary, Legacy(plan)])
      )
    );
  }

  [Theory]
  [InlineData("truck")]
  [InlineData("stop-truck")]
  [InlineData("receipt")]
  [InlineData("duplicate")]
  public void AmbiguousFutureAssignmentsAreNotSilentlyAppended(string change)
  {
    var plan = Plan();
    var current = Delivery(Load(plan));
    var next = Legacy(plan);
    if (change == "truck")
      next.TruckId = Guid.NewGuid();
    if (change == "stop-truck")
      next.Stops[0].TruckId = Guid.NewGuid();
    if (change == "receipt")
      next.AwaitingReceipt = true;
    var loads = new List<DispatchResponse> { current, next };
    if (change == "duplicate")
      loads.Add(next);
    Assert.Throws<RoutePlanningException>(
      () => FuelHorizon.SelectLoads(plan, loads)
    );
  }

  [Theory]
  [InlineData("unchanged", true)]
  [InlineData("truck", false)]
  [InlineData("native", false)]
  [InlineData("stop", false)]
  [InlineData("itinerary-scope", false)]
  public void NativeHorizonProjectionChecksEachFutureLoadScope(
    string change,
    bool valid
  )
  {
    var plan = Plan();
    var current = Delivery(Load(plan));
    var next = Legacy(plan);
    var loads = new[] { current, next };
    var saved = Snapshot(plan);
    saved.Plan.DispatchIds.Add(next.Id);
    foreach (var load in loads)
      saved.Plan.DispatchSignatures[load.Id] = FuelHorizon.LoadSignature(load);
    var itinerary = loads
      .SelectMany(load =>
        load.Stops.Select(stop => new FuelItineraryStop(
          load.Id,
          new(stop.Id, "", "", stop.Sequence, new(40, -80)),
          100
        )
        {
          ExecutionLegId = load.ExecutionLegId,
          AssignmentRevision = load.AssignmentRevision,
        })
      )
      .ToList();
    if (change == "truck")
      next.TruckId = Guid.NewGuid();
    if (change == "native")
      next.ExecutionLegId = Guid.NewGuid();
    if (change == "stop")
      next.Stops[0].Id = Guid.NewGuid();
    if (change == "itinerary-scope")
      itinerary[^1] = itinerary[^1] with
      {
        ExecutionLegId = plan.ExecutionLegId,
        AssignmentRevision = plan.AssignmentRevision,
      };
    Assert.Equal(
      valid,
      FuelPlanProjection.AssignmentsMatch(saved.Plan, plan.DispatchId, loads)
        && FuelPlanProjection.RemainingStopsMatch(
          itinerary,
          plan.DispatchId,
          current.Stops[0].Id,
          loads
        )
    );
  }

  [Fact]
  public void NativeSnapshotCanRollIntoItsExactSavedLegacyContinuation()
  {
    var plan = Plan();
    var next = Legacy(plan);
    var saved = Snapshot(plan);
    saved.Plan.DispatchIds.Add(next.Id);
    saved.Plan.DispatchSignatures[next.Id] = FuelHorizon.LoadSignature(next);
    saved = saved with
    {
      Stops = next
        .Stops.Select(stop => new FuelItineraryStop(
          next.Id,
          new(stop.Id, "", "", stop.Sequence, new(40, -80)),
          100
        ))
        .ToArray(),
    };
    var continuation = new RoutePlan
    {
      TruckId = plan.TruckId,
      DispatchId = next.Id,
    };
    Assert.True(FuelPlanProjection.SameScope(saved, continuation));
    Assert.True(
      FuelPlanProjection.AssignmentsMatch(saved.Plan, next.Id, [next])
    );
    continuation.ExecutionLegId = Guid.NewGuid();
    Assert.False(FuelPlanProjection.SameScope(saved, continuation));
  }

  private static DispatchResponse Delivery(DispatchResponse load)
  {
    load.Stops =
    [
      new()
      {
        Id = Guid.NewGuid(),
        TruckId = load.TruckId,
        Sequence = 1,
        Job = "Drop Off",
      },
    ];
    return load;
  }

  private static DispatchResponse Legacy(RoutePlan plan) =>
    Delivery(
      new()
      {
        Id = Guid.NewGuid(),
        TruckId = plan.TruckId,
        Status = "assigned",
      }
    );

  private static RoutePlan Plan() =>
    new()
    {
      TruckId = Guid.NewGuid(),
      DispatchId = Guid.NewGuid(),
      ExecutionLegId = Guid.NewGuid(),
      AssignmentRevision = 4,
    };

  private static DispatchResponse Load(RoutePlan plan) =>
    new()
    {
      Id = plan.DispatchId,
      TruckId = plan.TruckId,
      ExecutionLegId = plan.ExecutionLegId,
      AssignmentRevision = plan.AssignmentRevision,
      ExecutionStatus = "active",
    };

  private static TruckFuelPlanSnapshot Snapshot(RoutePlan plan) =>
    new(
      plan.TruckId,
      plan.DispatchId,
      DateTime.UtcNow,
      new()
      {
        TruckId = plan.TruckId,
        ExecutionLegId = plan.ExecutionLegId,
        AssignmentRevision = plan.AssignmentRevision,
        DispatchIds = [plan.DispatchId],
        Stops = [new() { StationId = Guid.NewGuid() }],
      },
      [],
      null
    )
    {
      RootExecutionLegId = plan.ExecutionLegId,
      AssignmentRevision = plan.AssignmentRevision,
    };
}
