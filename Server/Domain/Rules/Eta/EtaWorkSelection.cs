using Domain.Models.Eta;
using Domain.Models.Execution;
using Domain.Models.Routing;
using Domain.Rules.Routing;

namespace Domain.Rules.Eta;

// Which of a truck's work the forecast may follow, and why it stops
// following. A chain is a run: it starts at the work in hand and continues
// only while each next piece is still the same run by the same driver. Once
// one piece cannot be followed, nothing behind it can either - an arrival
// time computed across a gap would be a guess wearing a clock face.
public static class EtaWorkSelection
{
  // A reason that does not only skip this piece of work but ends the run:
  // what follows it is unreachable, not merely unknown.
  public static bool EndsTheRun(EtaWorkExclusionReason reason) =>
    reason
      is EtaWorkExclusionReason.NativeNotActive
        or EtaWorkExclusionReason.NativeConnectionRequired
        or EtaWorkExclusionReason.UnresolvedWork;

  public static EtaWorkExclusionReason? Exclusion(
    TruckWorkSegment segment,
    IReadOnlyList<RouteWorkSnapshot> selected,
    bool blocked
  )
  {
    if (blocked)
      return EtaWorkExclusionReason.BlockedByEarlierWork;
    if (!segment.Work.ExecutionLegId.HasValue)
    {
      if (segment.Status is not ("assigned" or "in_transit"))
        return EtaWorkExclusionReason.LegacyNotAssigned;
      if (segment.IsOverdue)
        return EtaWorkExclusionReason.OverdueUpcoming;
    }
    else if (!PlanningWorkPolicy.HasOpenAssignment(segment))
      return EtaWorkExclusionReason.NativeNotActive;
    // Work accepted into execution ahead of this one is still this truck's
    // work, and the forecast can follow it: those stops carry their own
    // appointments and service, and the clock keeps its rests across them.
    // Where a run ends is one question, asked in one place - here and in the
    // fuel horizon alike.
    if (selected.Count > 0 && !PlanningWorkPolicy.ContinuesTheRun(segment))
      return EtaWorkExclusionReason.NativeConnectionRequired;
    return PlanningWorkPolicy.BlockingProblem(segment) is null
      ? null
      : EtaWorkExclusionReason.UnresolvedWork;
  }
}
