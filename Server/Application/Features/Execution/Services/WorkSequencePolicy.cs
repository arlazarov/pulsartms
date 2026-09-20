using System.Collections.Immutable;
using Application.Features.Execution.Models;
using Domain.Entities.Execution;

namespace Application.Features.Execution.Services;

public static class WorkSequencePolicy
{
  public static WorkSequenceAssessment Assess(
    IReadOnlyList<IWorkFacts> ordered,
    WorkSequenceEvidence evidence
  )
  {
    var precedence = ImmutableArray.CreateBuilder<WorkPrecedence>();
    var issues = ImmutableArray.CreateBuilder<WorkSequenceIssue>();
    var identities = ordered.Select(Identity).ToHashSet();
    var transfers = evidence
      .Transfers.Where(x =>
        identities.Contains(new(x.DispatchId, x.OutgoingLegId))
        || identities.Contains(new(x.DispatchId, x.IncomingLegId))
      )
      .ToImmutableArray();
    void Block(IWorkFacts load, WorkSequenceProblem problem)
    {
      var issue = new WorkSequenceIssue(Identity(load), problem);
      if (!issues.Contains(issue))
        issues.Add(issue);
    }
    foreach (var load in ordered.Where(x => x.ExecutionLegId.HasValue))
    {
      if (Position(load, evidence) is null)
        Block(load, WorkSequenceProblem.MissingExecutionLink);
      if (
        transfers.Any(x =>
          x.DispatchId == load.Id
          && x.IncomingLegId == load.ExecutionLegId
          && (!x.ReleaseConfirmed || !x.ReceiptConfirmed)
        )
      )
        Block(load, WorkSequenceProblem.AwaitingTransfer);
    }
    // An uncertain later candidate may precede any earlier future candidate.
    for (var i = 0; i < ordered.Count; i++)
    {
      var previous = ordered[i];
      for (var j = i + 1; j < ordered.Count; j++)
      {
        var next = ordered[j];
        var basis = Basis(previous, next, evidence);
        if (basis is { } known)
        {
          if (j == i + 1)
            precedence.Add(new(Identity(previous), Identity(next), known));
          continue;
        }
        var problem =
          Started(previous) && Started(next)
            ? WorkSequenceProblem.CompetingCurrentWork
          : ReverseOrderKnown(previous, next, evidence)
            ? WorkSequenceProblem.ConflictingOrder
          : WorkSequenceProblem.UnknownOrder;
        Block(previous, problem);
      }
    }
    return new(precedence.ToImmutable(), issues.ToImmutable(), transfers);
  }

  private static WorkPrecedenceBasis? Basis(
    IWorkFacts previous,
    IWorkFacts next,
    WorkSequenceEvidence evidence
  )
  {
    if (NativeOrder(previous, next, evidence) is { } nativeOrder)
      return nativeOrder < 0 ? WorkPrecedenceBasis.ExecutionLink : null;
    if (Started(previous) && !Started(next))
      return WorkPrecedenceBasis.CurrentActivity;
    // Two loads neither of which has started are ordered by the appointments
    // they are booked for. This used to be asked only of loads with no
    // execution leg, on the understanding that accepting work into execution
    // would carry its own order; accepted days ahead, as they now are, three
    // loads with three ship dates had no order at all and blocked the whole
    // forecast. Where the schedule does not say - equal dates and no times,
    // a missing date, two time zones - it still returns nothing, and nothing
    // is guessed.
    if (
      !Started(previous)
      && !Started(next)
      && ScheduleOrder(previous, next) < 0
    )
      return WorkPrecedenceBasis.ScheduledStart;
    return null;
  }

  private static bool ReverseOrderKnown(
    IWorkFacts previous,
    IWorkFacts next,
    WorkSequenceEvidence evidence
  ) =>
    NativeOrder(previous, next, evidence) > 0
    || !Started(previous) && Started(next)
    || ScheduleOrder(previous, next) > 0;

  private static int? NativeOrder(
    IWorkFacts previous,
    IWorkFacts next,
    WorkSequenceEvidence evidence
  ) =>
    previous.Id == next.Id
    && Position(previous, evidence) is { } first
    && Position(next, evidence) is { } second
      ? first.Sequence.CompareTo(second.Sequence)
      : null;

  private static WorkLegPosition? Position(
    IWorkFacts load,
    WorkSequenceEvidence evidence
  ) =>
    evidence.Legs.SingleOrDefault(x =>
      x.DispatchId == load.Id && x.ExecutionLegId == load.ExecutionLegId
    );

  private static int? ScheduleOrder(IWorkFacts previous, IWorkFacts next)
  {
    var first = previous.Stops.OrderBy(x => x.Sequence).FirstOrDefault();
    var second = next.Stops.OrderBy(x => x.Sequence).FirstOrDefault();
    var firstDate = first?.ScheduledDate ?? previous.ShipDate;
    var secondDate = second?.ScheduledDate ?? next.ShipDate;
    if (
      firstDate is null
      || secondDate is null
      || first?.AppointmentTimeZoneId != second?.AppointmentTimeZoneId
    )
      return null;
    var dateOrder = firstDate.Value.CompareTo(secondDate.Value);
    if (dateOrder != 0)
      return dateOrder;
    return
      first?.ScheduledTime is { } firstTime
      && second?.ScheduledTime is { } secondTime
      ? firstTime.CompareTo(secondTime)
      : null;
  }

  private static bool Started(IWorkFacts load) =>
    load.ExecutionLegId.HasValue
      ? load.ExecutionStatus == "active"
      : load.Status.Equals("in_transit", StringComparison.OrdinalIgnoreCase)
        || load.Stops.Any(x =>
          x.PickedUpAt.HasValue || x.ManualCompletedAt.HasValue
        );

  private static WorkIdentity Identity(IWorkFacts load) =>
    new(load.Id, load.ExecutionLegId);
}
