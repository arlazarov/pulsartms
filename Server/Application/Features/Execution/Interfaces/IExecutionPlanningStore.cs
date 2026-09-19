using Domain.Entities.Execution;

namespace Application.Features.Execution.Interfaces;

public interface IExecutionPlanningStore
{
  Task<ExecutionPlanningChange?> ClaimAsync(DateTime now, CancellationToken ct);

  Task<bool> CompleteAsync(
    ExecutionPlanningChange work,
    DateTime now,
    bool succeeded,
    CancellationToken ct
  );

  Task PruneAsync(DateTime before, CancellationToken ct);
}
