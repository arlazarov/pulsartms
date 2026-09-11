using Application.Features.Routing.Models;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Application.Features.Routing.Interfaces;

public interface INextLoadRouteReader
{
  Task<IReadOnlyList<Load>> ReadLoadsAsync(Guid truckId, CancellationToken ct);
  Task<IReadOnlyList<NextLoadRouteVersion>> ReadVersionsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);
  Task<IReadOnlyDictionary<Guid, SavedNextLoadRoute>> ReadGeometryAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);
}
