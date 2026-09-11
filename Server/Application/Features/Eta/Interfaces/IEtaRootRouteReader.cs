using Application.Features.Eta.Models;

namespace Application.Features.Eta.Interfaces;

public interface IEtaRootRouteReader
{
  Task<EtaRootRouteMetadata?> ReadAsync(Guid dispatchId, CancellationToken ct);
}
