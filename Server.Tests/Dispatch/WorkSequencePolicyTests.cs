using Application.Features.Execution.Services;
using Domain.Models.Execution;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Unit")]
public sealed class WorkSequencePolicyTests
{
  [Fact]
  public void LoadNumberCannotResolveEqualAppointments()
  {
    var first = Future(8);
    var second = Future(8);
    first.LoadNumber = 1;
    second.LoadNumber = 999;

    var result = Assess(first, second);

    Assert.Empty(result.Precedence);
    Assert.Equal(
      WorkSequenceProblem.UnknownOrder,
      Assert.Single(result.Issues).Problem
    );
    Assert.Equal(first.Id, result.Issues[0].Work.DispatchId);
  }

  [Fact]
  public void UnknownFutureOrderPreservesTheUniqueCurrentWork()
  {
    var current = Future(8);
    current.Status = "in_transit";
    var next = Future(12);
    var unknown = Future(16);
    unknown.Stops[0].ScheduledDate = null;

    var result = Assess(current, next, unknown);

    Assert.Equal(next.Id, Assert.Single(result.Issues).Work.DispatchId);
    Assert.Equal(
      WorkPrecedenceBasis.CurrentActivity,
      Assert.Single(result.Precedence).Basis
    );
  }

  [Fact]
  public void CompetingStartedLoadsHaveNoArbitraryCurrentWork()
  {
    var first = Future(8);
    var second = Future(12);
    first.Status = "in_transit";
    second.Stops[0].PickedUpAt = DateTime.UtcNow;

    var result = Assess(first, second);

    Assert.Equal(
      WorkSequenceProblem.CompetingCurrentWork,
      Assert.Single(result.Issues).Problem
    );
  }

  [Fact]
  public void DistinctAppointmentsRemainProvisionalEvidence()
  {
    var result = Assess(Future(8), Future(12));

    Assert.Empty(result.Issues);
    Assert.Equal(
      WorkPrecedenceBasis.ScheduledStart,
      Assert.Single(result.Precedence).Basis
    );
  }

  [Fact]
  public void LocalTimesInDifferentZonesCannotProveOrder()
  {
    var first = Future(8);
    var second = Future(12);
    first.Stops[0].AppointmentTimeZoneId = "America/Toronto";
    second.Stops[0].AppointmentTimeZoneId = "America/Vancouver";

    Assert.Equal(
      WorkSequenceProblem.UnknownOrder,
      Assert.Single(Assess(first, second).Issues).Problem
    );
  }

  [Fact]
  public void MissingTimeDoesNotMeanMidnight()
  {
    var first = Future(8);
    first.Stops[0].ScheduledTime = null;

    Assert.Equal(
      WorkSequenceProblem.UnknownOrder,
      Assert.Single(Assess(first, Future(12)).Issues).Problem
    );
  }

  [Fact]
  public void ASingleUnscheduledLoadHasNoOrderingConflict()
  {
    var only = Future(8);
    only.Stops[0].ScheduledDate = null;

    Assert.Empty(Assess(only).Issues);
  }

  [Fact]
  public void ReversedAppointmentsAreReportedWithoutReordering()
  {
    var first = Future(12);
    var second = Future(8);

    Assert.Equal(
      new(new(first.Id, null), WorkSequenceProblem.ConflictingOrder),
      Assert.Single(Assess(first, second).Issues)
    );
  }

  // Accepting work into execution was taken to carry its own order, so the
  // appointments of accepted loads were not read at all. Accepted days
  // ahead, as they now are, three loads with three ship dates had no order
  // between them and blocked the whole forecast: 11006 could be told nothing
  // about the loads it was to drive on the 21st, the 23rd and the 25th.
  [Fact]
  public void AcceptedLoadsAreOrderedByTheAppointmentsTheyAreBookedFor()
  {
    var first = Future(8);
    var second = Future(12);
    first.ExecutionLegId = Guid.NewGuid();
    second.ExecutionLegId = Guid.NewGuid();
    first.ExecutionStatus = "planned";
    second.ExecutionStatus = "planned";

    var result = WorkSequencePolicy.Assess(
      [first, second],
      new([Position(first, 1), Position(second, 1)], [])
    );

    Assert.Empty(result.Issues);
    Assert.Equal(
      WorkPrecedenceBasis.ScheduledStart,
      Assert.Single(result.Precedence).Basis
    );
  }

  // And where the schedule does not say, nothing is guessed - not for
  // accepted work either.
  [Theory]
  [InlineData("equal")]
  [InlineData("no-time")]
  [InlineData("other-zone")]
  public void AnAcceptedLoadWithoutAScheduleStillHasNoOrder(string missing)
  {
    var first = Future(8);
    var second = Future(missing == "equal" ? 8 : 12);
    first.ExecutionLegId = Guid.NewGuid();
    second.ExecutionLegId = Guid.NewGuid();
    if (missing == "no-time")
      first.Stops[0].ScheduledTime = null;
    if (missing == "other-zone")
      second.Stops[0].AppointmentTimeZoneId = "America/Vancouver";

    var result = WorkSequencePolicy.Assess(
      [first, second],
      new([Position(first, 1), Position(second, 1)], [])
    );

    Assert.Equal(
      WorkSequenceProblem.UnknownOrder,
      Assert.Single(result.Issues).Problem
    );
  }

  [Fact]
  public void ExplicitLegSequenceTakesPrecedenceOverAppointments()
  {
    var (first, next, evidence) = NativePair();

    var result = WorkSequencePolicy.Assess([first, next], evidence);

    Assert.Empty(result.Issues);
    Assert.Equal(
      WorkPrecedenceBasis.ExecutionLink,
      Assert.Single(result.Precedence).Basis
    );
  }

  [Fact]
  public void ActivityCannotOverrideAReversedExplicitLink()
  {
    var (first, next, evidence) = NativePair();
    next.ExecutionStatus = "active";

    var result = WorkSequencePolicy.Assess([next, first], evidence);

    Assert.Equal(
      WorkSequenceProblem.ConflictingOrder,
      Assert.Single(result.Issues).Problem
    );
  }

  [Theory]
  [InlineData(false, false)]
  [InlineData(true, false)]
  [InlineData(false, true)]
  public void IncomingLegRequiresBothTransferConfirmations(
    bool released,
    bool received
  )
  {
    var (first, next, evidence) = NativePair();
    evidence = evidence with
    {
      Transfers =
      [
        new(
          Guid.NewGuid(),
          first.Id,
          first.ExecutionLegId!.Value,
          next.ExecutionLegId!.Value,
          released,
          received,
          1,
          Guid.NewGuid(),
          Guid.NewGuid(),
          Guid.NewGuid(),
          null,
          null,
          null,
          null
        ),
      ],
    };

    var result = WorkSequencePolicy.Assess([next], evidence);

    Assert.Equal(
      WorkSequenceProblem.AwaitingTransfer,
      Assert.Single(result.Issues).Problem
    );
    Assert.Single(result.Transfers);
  }

  [Fact]
  public void AClaimedNativeLegRequiresItsPersistedLoadLink()
  {
    var load = Future(8);
    load.ExecutionLegId = Guid.NewGuid();

    Assert.Equal(
      WorkSequenceProblem.MissingExecutionLink,
      Assert.Single(Assess(load).Issues).Problem
    );
  }

  private static WorkSequenceAssessment Assess(params Load[] loads) =>
    WorkSequencePolicy.Assess(loads, WorkSequenceEvidence.Empty);

  private static (
    Load First,
    Load Next,
    WorkSequenceEvidence Evidence
  ) NativePair()
  {
    var first = Future(12);
    var next = Future(8);
    next.Id = first.Id;
    first.ExecutionLegId = Guid.NewGuid();
    next.ExecutionLegId = Guid.NewGuid();
    return (first, next, new([Position(first, 1), Position(next, 2)], []));
  }

  private static WorkLegPosition Position(Load load, int sequence) =>
    new(
      load.Id,
      load.ExecutionLegId!.Value,
      sequence,
      Guid.NewGuid(),
      load.ExecutionStatus ?? "planned",
      1,
      load.Stops[0].Id,
      load.Stops[^1].Id
    );

  private static Load Future(int hour) =>
    new()
    {
      Id = Guid.NewGuid(),
      Status = "assigned",
      Stops =
      [
        new()
        {
          Id = Guid.NewGuid(),
          Sequence = 1,
          ScheduledDate = new(2026, 9, 16),
          ScheduledTime = new(hour, 0),
          AppointmentTimeZoneId = "Etc/UTC",
        },
      ],
    };
}
