using System.Collections.Immutable;
using Application.Features.Dispatch.Models;
using Application.Features.Execution.Queries;
using Application.Features.Routing.Services.Routes;
using Domain.Models.Execution;
using Server.Tests.Support;

namespace Server.Tests.Messaging;

// Messenger places a driver's loads as the board does (stage 3b of
// docs/architecture/current-work.md): the current load first, then work
// planning has passed - shown with its conflict, no longer dropped, and
// ahead of what a list limit cuts - then the work after the current, each
// compared at the accepted revision.
[Trait("Category", "Messaging")]
[Trait("Kind", "Unit")]
public sealed class DriverWorkOrderTests
{
  private static readonly Guid Truck = Guid.NewGuid();
  private static readonly DispatchResponse PassedLoad = Load(1403, 7);
  private static readonly DispatchResponse CurrentLoad = Load(1410, 3);
  private static readonly DispatchResponse LaterLoad = Load(1391, 2);
  private static readonly WorkLoadReference Passed = Reference(PassedLoad);
  private static readonly WorkLoadReference Current = Reference(CurrentLoad);
  private static readonly WorkLoadReference Later = Reference(LaterLoad);
  private static readonly WorkLoadReference[] Board = [Passed, Current, Later];

  [Fact]
  public void CurrentLeadsThenPassedWorkWithItsConflictThenTheWorkAfterIt()
  {
    Assert.Equal(
      [
        (1410, "current", null),
        (1403, "earlier", "route_passed_not_delivered"),
        (1391, "next", null),
      ],
      Placed(DriverWorkOrder.Apply(Board, Inputs()))
    );
  }

  [Fact]
  public void ALoadReadAtAnotherRevisionIsStaleNotCurrent()
  {
    var reassigned = Current with { AcceptedRevision = 4 };
    Assert.Equal(
      [
        (1403, "earlier", "route_passed_not_delivered"),
        (1391, "next", null),
        (1410, "stale", null),
      ],
      Placed(DriverWorkOrder.Apply([Passed, reassigned, Later], Inputs()))
    );
  }

  // An older load without an execution leg never stores its own
  // AssignmentRevision; the itinerary accepts it at its planning revision,
  // and so does Messenger now (divergence 6).
  [Fact]
  public void AnOlderLoadIsComparedAtItsPlanningRevision()
  {
    var legacy = Load(1420, 5);
    legacy.ExecutionLegId = null;
    var inputs = Inputs(legacy);
    var reference = Reference(legacy) with { AssignmentRevision = 0 };

    Assert.Equal(
      [(1420, "current", null)],
      Placed(DriverWorkOrder.Apply([reference], inputs))
    );
  }

  [Fact]
  public void WithoutInputsTheBoardsOrderStandsWhole()
  {
    Assert.Equal(
      [(1403, null, null), (1410, null, null), (1391, null, null)],
      Placed(DriverWorkOrder.Apply(Board, null))
    );
  }

  // The owner's inputs: 1403's route passed, 1410 current, 1391 after it.
  private static TruckPlanningInputs Inputs()
  {
    var itinerary = FuelWorkFixture
      .Capture(Truck, [PassedLoad, CurrentLoad, LaterLoad])
      .Itinerary;
    return new(itinerary, null)
    {
      CurrentWork = new(CurrentLoad.Id, CurrentLoad.ExecutionLegId),
      CurrentAssignmentRevision = CurrentLoad.AssignmentRevision,
      PassedWork =
      [
        new(
          new(PassedLoad.Id, PassedLoad.ExecutionLegId),
          PassedLoad.AssignmentRevision
        ),
      ],
    };
  }

  private static TruckPlanningInputs Inputs(DispatchResponse only) =>
    new(FuelWorkFixture.Capture(Truck, [only]).Itinerary, null)
    {
      CurrentWork = new(only.Id, only.ExecutionLegId),
      CurrentAssignmentRevision = only.AssignmentRevision,
    };

  private static (int, string?, string?)[] Placed(
    IEnumerable<(WorkLoadReference Load, string? Phase, string? Conflict)> loads
  ) => [.. loads.Select(x => (x.Load.LoadNumber, x.Phase, x.Conflict))];

  private static DispatchResponse Load(int number, long revision) =>
    new()
    {
      Id = Guid.NewGuid(),
      ExecutionLegId = Guid.NewGuid(),
      ExecutionStatus = "active",
      TruckId = Truck,
      LoadNumber = number,
      AssignmentRevision = revision,
    };

  private static WorkLoadReference Reference(DispatchResponse load) =>
    new(
      load.Id,
      load.ExecutionLegId,
      load.AssignmentRevision,
      load.ExecutionStatus,
      load.LoadNumber,
      "",
      "",
      "",
      null,
      new(WorkActivity.Upcoming, DateTime.UnixEpoch, load.LoadNumber),
      ImmutableArray<WorkVisitReference>.Empty
    )
    {
      AcceptedRevision = load.AssignmentRevision,
    };
}
