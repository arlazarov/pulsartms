using System.Collections.Immutable;
using Application.Features.Execution.Queries;
using Application.Features.Routing.Services.Routes;
using Domain.Models.Execution;

namespace Server.Tests.Messaging;

// Messenger's "Current load" follows planning's current work, and drops
// only the loads planning says it has moved past, each only when the board
// read it at the same assignment (root review, September 27).
[Trait("Category", "Messaging")]
[Trait("Kind", "Unit")]
public sealed class DriverWorkOrderTests
{
  private static readonly WorkLoadReference Passed = Load(1403, 7);
  private static readonly WorkLoadReference Current = Load(1410, 3);
  private static readonly WorkLoadReference Later = Load(1391, 2);
  private static readonly WorkLoadReference[] Board = [Passed, Current, Later];

  [Fact]
  public void CurrentLeadsAndPassedWorkIsDropped()
  {
    Assert.Equal(
      [1410, 1391],
      Numbers(DriverWorkOrder.Apply(Board, Inputs(3, 7)))
    );
  }

  [Fact]
  public void AReassignedPassedLoadStays()
  {
    Assert.Equal(
      [1410, 1403, 1391],
      Numbers(DriverWorkOrder.Apply(Board, Inputs(3, 6)))
    );
  }

  [Fact]
  public void AReassignedCurrentLoadIsNotMovedAhead()
  {
    // Planning chose 1410 at revision 2; the board reads revision 3.
    Assert.Equal(
      [1391, 1410],
      Numbers(DriverWorkOrder.Apply([Later, Current], Inputs(2, 7)))
    );
  }

  [Fact]
  public void OrderingAloneHidesNothing()
  {
    // No current work and nothing passed: the board's order, whole.
    Assert.Equal(
      [1403, 1410, 1391],
      Numbers(DriverWorkOrder.Apply(Board, Inputs(null, null)))
    );
    Assert.Equal(
      [1403, 1410, 1391],
      Numbers(DriverWorkOrder.Apply(Board, null))
    );
  }

  private static TruckPlanningInputs Inputs(
    long? currentRevision,
    long? passedRevision
  ) =>
    new(null!, null)
    {
      CurrentWork = currentRevision is null
        ? null
        : new(Current.Id, Current.ExecutionLegId),
      CurrentAssignmentRevision = currentRevision,
      PassedWork = passedRevision is { } revision
        ? [new(new(Passed.Id, Passed.ExecutionLegId), revision)]
        : [],
    };

  private static int[] Numbers(IEnumerable<WorkLoadReference> loads) =>
    [.. loads.Select(x => x.LoadNumber)];

  private static WorkLoadReference Load(int number, long revision) =>
    new(
      Guid.NewGuid(),
      Guid.NewGuid(),
      revision,
      "active",
      number,
      "",
      "",
      "",
      null,
      new(WorkActivity.Upcoming, DateTime.UnixEpoch, number),
      ImmutableArray<WorkVisitReference>.Empty
    );
}
