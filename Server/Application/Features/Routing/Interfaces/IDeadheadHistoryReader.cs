using Application.Features.Routing.Models;

namespace Application.Features.Routing.Interfaces;

public interface IDeadheadHistoryReader
{
  Task<IReadOnlyDictionary<Guid, DeadheadHistorySnapshot>> ReadAsync(IReadOnlyCollection<Guid> dispatchIds, CancellationToken ct);
  Task<IReadOnlyDictionary<Guid, DeadheadHistorySnapshot>> ReadLoadedAsync(
    IReadOnlyCollection<Domain.Entities.Dispatch.Dispatch> current, CancellationToken ct);
}
