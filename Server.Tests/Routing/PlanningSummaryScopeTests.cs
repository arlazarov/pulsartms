using Application.Features.Dispatch.Models;
using Application.Features.Routing.Services.Routes;
using Domain.Models.Execution;
using Server.Tests.Support;

namespace Server.Tests.Routing;

// A summary with no result of its own - cold, or refused - speaks for the
// truck's current work as its planning inputs chose it. It used to take the
// first candidate, which can be work planning has moved past: a truck whose
// first load's route was fully passed then showed that load while the map
// and Messenger showed the next (architecture stage 1, September 27).
[Trait("Category", "Routing")]
[Trait("Kind", "Unit")]
public sealed class PlanningSummaryScopeTests
{
  private static readonly Guid Truck = Guid.NewGuid();
  private static readonly DispatchResponse Passed = Load(1395, 3);
  private static readonly DispatchResponse Current = Load(1412, 5);

  [Fact]
  public void AColdSummaryNamesTheCurrentWorkNotAPassedFirstCandidate()
  {
    var work = Inputs(current: Current);

    var cold = Reader().Read(work);

    Assert.True(cold.IsRefreshing);
    Assert.Equal(Current.Id, cold.DispatchId);
    Assert.Equal(Current.ExecutionLegId, cold.ExecutionLegId);
    Assert.Equal(Current.AssignmentRevision, cold.AssignmentRevision);
    // The same identity, leg and revision the owner chose.
    Assert.Equal(
      work.CurrentWork,
      new(cold.DispatchId!.Value, cold.ExecutionLegId)
    );
    Assert.Equal(work.CurrentAssignmentRevision, cold.AssignmentRevision);
  }

  [Fact]
  public void WithEveryCandidatePassedAColdSummaryNamesNoWork()
  {
    var cold = Reader().Read(Inputs(current: null));

    Assert.Null(cold.DispatchId);
    Assert.Null(cold.ExecutionLegId);
    Assert.Equal(0, cold.AssignmentRevision);
  }

  // Asked about a load by name - an earlier one included - the summary
  // speaks for that load.
  [Fact]
  public void AnExplicitDispatchIsKeptEvenWhenPlanningHasPassedIt()
  {
    var cold = Reader().Read(Inputs(current: Current), Passed.Id);

    Assert.Equal(Passed.Id, cold.DispatchId);
    Assert.Equal(Passed.ExecutionLegId, cold.ExecutionLegId);
    Assert.Equal(Passed.AssignmentRevision, cold.AssignmentRevision);
  }

  // The refused result is built by the same scope; asked directly, it
  // agrees with the cold summary in every case above.
  [Theory]
  [InlineData(true, false)]
  [InlineData(false, false)]
  [InlineData(true, true)]
  public void TheRefusedScopeIsTheColdScope(
    bool hasCurrent,
    bool explicitPassed
  )
  {
    var work = Inputs(hasCurrent ? Current : null);
    Guid? dispatch = explicitPassed ? Passed.Id : null;

    var scope = PlanningSummaryReader.Scope(work, dispatch);
    var cold = Reader().Read(work, dispatch);

    Assert.Equal(cold.DispatchId, scope?.Work.DispatchId);
    Assert.Equal(cold.ExecutionLegId, scope?.Work.ExecutionLegId);
    Assert.Equal(cold.AssignmentRevision, scope?.AssignmentRevision ?? 0);
  }

  // No reads: the inputs reader and route service are absent, and a cold
  // summary is built from the inputs it was handed.
  private static PlanningSummaryReader Reader() =>
    new(
      new PlanningSummaryCache(TimeProvider.System),
      null!,
      null!,
      new TestCompany(),
      TestCache.Create()
    );

  private static TruckPlanningInputs Inputs(DispatchResponse? current)
  {
    var itinerary = FuelWorkFixture.Capture(Truck, [Passed, Current]).Itinerary;
    return new(itinerary, null)
    {
      CurrentWork = current is null
        ? null
        : new(current.Id, current.ExecutionLegId),
      CurrentAssignmentRevision = current?.AssignmentRevision,
      PassedWork =
        current == Current
          ?
          [
            new(
              new(Passed.Id, Passed.ExecutionLegId),
              Passed.AssignmentRevision
            ),
          ]
          :
          [
            new(
              new(Passed.Id, Passed.ExecutionLegId),
              Passed.AssignmentRevision
            ),
            new(
              new(Current.Id, Current.ExecutionLegId),
              Current.AssignmentRevision
            ),
          ],
    };
  }

  private static DispatchResponse Load(int number, long revision) =>
    new()
    {
      Id = Guid.NewGuid(),
      ExecutionLegId = Guid.NewGuid(),
      ExecutionStatus = "planned",
      TruckId = Truck,
      LoadNumber = number,
      AssignmentRevision = revision,
      Status = "assigned",
    };
}
