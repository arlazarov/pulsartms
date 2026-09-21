using Application.Features.Execution.Models;
using Domain.Entities.Execution;

namespace Server.Tests.Dispatch;

// ExecutionStopRows reads one leg. Handoff confirmation lives with the
// transfer participants, so this read cannot observe it and leaves the fact
// at its default. These checks pin that boundary: a leg-scoped row is not
// evidence that a stop is complete when a handoff may still be pending.
[Trait("Category", "Dispatch")]
[Trait("Kind", "Unit")]
public sealed class LegScopedStopFactsTests
{
  [Theory]
  [InlineData(true, null)]
  [InlineData(false, true)]
  public void LegScopedRowsReportCompletionWithoutHandoffEvidence(
    bool executionCompleted,
    bool? completionOverride
  )
  {
    var leg = Leg(executionCompleted, completionOverride);

    var row = Assert.Single(ExecutionStopRows.Read(leg));

    Assert.False(row.AwaitingHandoff);
    Assert.True(row.IsCompleted);
  }

  [Theory]
  [InlineData(true, null)]
  [InlineData(false, true)]
  public void ObservedHandoffKeepsTheSameRowOpen(
    bool executionCompleted,
    bool? completionOverride
  )
  {
    var leg = Leg(executionCompleted, completionOverride);

    var row = Assert.Single(ExecutionStopRows.Read(leg));
    row.AwaitingHandoff = true;

    Assert.False(row.IsCompleted);
  }

  private static ExecutionLeg Leg(
    bool executionCompleted,
    bool? completionOverride
  ) =>
    new()
    {
      Id = Guid.NewGuid(),
      TruckId = Guid.NewGuid(),
      Stops =
      [
        new()
        {
          Id = Guid.NewGuid(),
          DispatchId = Guid.NewGuid(),
          Position = 0,
          Job = "Pick Up",
          StateAfter = "Loaded",
          ExecutionCompleted = executionCompleted,
          CompletionOverride = completionOverride,
        },
      ],
    };
}
