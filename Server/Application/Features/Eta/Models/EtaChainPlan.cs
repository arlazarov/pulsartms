using Application.Features.Eta.Algorithms;
using Application.Features.Routing.Models;

namespace Application.Features.Eta.Models;

public sealed record EtaStopActivity(DateTime? ArrivedAt, DateTime? PickedUpAt, DateTime? DeliveredAt, DateTime? DepartedAt)
{
  public bool Completed => PickedUpAt.HasValue || DeliveredAt.HasValue || DepartedAt.HasValue;
}

public sealed record EtaFutureDispatch(Guid DispatchId, IReadOnlyList<PlanStop> Stops,
  EtaRouteTiming? Connection, EtaRouteTiming? Route, string? UnavailableReason);

public sealed record EtaChainPlan(string InputHash, IReadOnlyList<EtaFutureDispatch> Future,
  IReadOnlyDictionary<Guid, EtaStopActivity> CurrentActivities);
