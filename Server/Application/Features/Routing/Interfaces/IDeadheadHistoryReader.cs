using Domain.Models.Routing;

namespace Application.Features.Routing.Interfaces;

public interface IDeadheadHistoryReader
{
  Task<IReadOnlyDictionary<Guid, DeadheadHistorySource>> ReadAsync(
    IReadOnlyCollection<Guid> dispatchIds,
    CancellationToken ct
  );
  Task<IReadOnlyDictionary<Guid, DeadheadHistorySource>> ReadLoadedAsync(
    IReadOnlyCollection<RouteWorkSnapshot> current,
    CancellationToken ct
  );
}
