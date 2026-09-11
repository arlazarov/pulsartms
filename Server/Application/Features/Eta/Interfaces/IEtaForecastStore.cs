using Application.Features.Eta.Models;

namespace Application.Features.Eta.Interfaces;

public interface IEtaForecastStore
{
  Task<IReadOnlyList<EtaForecastSnapshot>> ReadAsync(IReadOnlyCollection<Guid> dispatchIds, CancellationToken ct);
  Task<bool> SaveAsync(IReadOnlyCollection<EtaForecastSnapshot> snapshots, CancellationToken ct);
}
