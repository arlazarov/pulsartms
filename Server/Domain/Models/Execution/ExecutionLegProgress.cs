using Domain.Entities.Dispatch;
using Domain.Entities.Execution;

namespace Domain.Models.Execution;

public static class ExecutionLegProgress
{
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
