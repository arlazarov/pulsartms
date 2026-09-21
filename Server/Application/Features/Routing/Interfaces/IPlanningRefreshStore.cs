using Domain.Models.Routing;

namespace Application.Features.Routing.Interfaces;

public interface IPlanningRefreshStore
{
  Task<PlanningRefreshState> RequestAsync(
    PlanningScope scope,
    string inputSignature,
    DateTime now,
    CancellationToken ct
  );

  Task<PlanningRefreshWork?> ClaimAsync(
    DateTime now,
    TimeSpan leaseDuration,
    CancellationToken ct
  );

  Task<bool> CompleteAsync(
    PlanningRefreshWork work,
    bool succeeded,
    DateTime now,
    DateTime nextAvailable,
    CancellationToken ct
  );

  Task PruneAsync(DateTime before, CancellationToken ct);
}
