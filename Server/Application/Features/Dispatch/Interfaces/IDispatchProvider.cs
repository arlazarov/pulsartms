using Application.Features.Dispatch.Models;

namespace Application.Features.Dispatch.Interfaces;

public interface IDispatchProvider
{
  Task<IReadOnlyList<ExternalDispatch>> GetDispatchesAsync(
    CancellationToken cancellationToken = default
  );

  Task<IReadOnlyList<ExternalDispatch>> GetDispatchesAsync(
    DateOnly from,
    DateOnly to,
    CancellationToken cancellationToken = default
  );
}
