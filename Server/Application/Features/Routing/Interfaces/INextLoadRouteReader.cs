using Domain.Models.Routing;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Application.Features.Routing.Interfaces;

public interface INextLoadRouteReader
{
  Task<IReadOnlyList<Load>> ReadLoadsAsync(Guid truckId, CancellationToken ct);
  Task<IReadOnlyList<NextLoadRouteVersion>> ReadVersionsAsync(
    IReadOnlyCollection<Guid> ids,
    CancellationToken ct
  );
  Task<IReadOnlyDictionary<Guid, SavedNextLoadRoute>> ReadGeometryAsync(
    IReadOnlyCollection<Guid> ids,
    CancellationToken ct
  );

  Task<IReadOnlyList<NextLoadRouteVersion>> ReadExecutionVersionsAsync(
    IReadOnlyCollection<Guid> executionLegIds,
    CancellationToken ct
  ) => throw new NotSupportedException("Execution-leg routes are unsupported.");

  Task<
    IReadOnlyDictionary<Guid, SavedNextLoadRoute>
  > ReadExecutionGeometryAsync(
    IReadOnlyCollection<Guid> executionLegIds,
    CancellationToken ct
  ) => throw new NotSupportedException("Execution-leg routes are unsupported.");
}
