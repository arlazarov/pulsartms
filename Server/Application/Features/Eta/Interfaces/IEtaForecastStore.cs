using Domain.Models.Eta;

namespace Application.Features.Eta.Interfaces;

public interface IEtaForecastStore
{
  Task<IReadOnlyList<EtaForecastSnapshot>> ReadAsync(
    IReadOnlyCollection<Guid> dispatchIds,
    CancellationToken ct
  );
  Task<bool> SaveAsync(
    IReadOnlyCollection<EtaForecastSnapshot> snapshots,
    CancellationToken ct
  );

  Task<IReadOnlyList<EtaForecastSnapshot>> ReadExecutionLegsAsync(
    IReadOnlyCollection<Guid> executionLegIds,
    CancellationToken ct
  ) =>
    throw new NotSupportedException("Execution-leg forecasts are unsupported.");
}
