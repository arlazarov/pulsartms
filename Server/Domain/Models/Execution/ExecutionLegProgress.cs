using Domain.Entities.Dispatch;
using Domain.Entities.Execution;

namespace Domain.Models.Execution;

public static class ExecutionLegProgress
{
  // Work the dispatcher took back (AMF1405, September 28): a leg that its
  // stops made active, with no stop done or arrived any more. One started
  // by a receipt or with a recorded start is active for its own reason.
  public static bool ActiveWithoutWork(
    ExecutionLeg leg,
    IReadOnlyList<DispatchStop> stops
  ) =>
    leg.Status == "active"
    && !leg.StartSwitchId.HasValue
    && !leg.StartedAt.HasValue
    && stops.Count > 0
    && stops.All(x => !x.IsCompleted && !x.ArrivedAt.HasValue);

  public static void Apply(
    ExecutionLeg leg,
    IReadOnlyList<DispatchStop> stops,
    IReadOnlySet<Guid>? confirmedTransfers = null
  )
  {
    // Receipt activates incoming work; cargo cannot release a handoff.
    if (leg.Status == "planned" && leg.StartSwitchId.HasValue)
      return;
    if (
      leg.Status == "planned"
      && stops.Any(x => x.IsCompleted || x.ArrivedAt.HasValue)
    )
      leg.Status = "active";
    else if (ActiveWithoutWork(leg, stops))
      leg.Status = "planned";
    if (
      leg.Status == "active"
      && !leg.EndSwitchId.HasValue
      && stops.Count > 0
      && stops.All(x =>
        x.IsCompleted || confirmedTransfers?.Contains(x.Id) == true
      )
      && stops[^1].Job is "Delivery" or "Drop Off"
    )
    {
      var last = stops[^1];
      leg.Status = "completed";
      leg.CompletedAt =
        last.CompletionOverride == true
          ? last.ManualCompletedAt
          : last.DepartedAt ?? last.DeliveredAt ?? last.ManualCompletedAt;
    }
  }
}
