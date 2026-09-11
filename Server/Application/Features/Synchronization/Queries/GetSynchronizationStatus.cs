using Application.Features.Synchronization.Interfaces;
using Application.Features.Synchronization.Models;
using Application.Models;

namespace Application.Features.Synchronization.Queries;

public sealed record GetSynchronizationStatusQuery : IRequest<RequestResponse<SynchronizationStatus>>;

public sealed class GetSynchronizationStatusHandler(ISynchronizationStatusProvider provider)
  : IRequestHandler<GetSynchronizationStatusQuery, RequestResponse<SynchronizationStatus>>
{
  public Task<RequestResponse<SynchronizationStatus>> Handle(GetSynchronizationStatusQuery request, CancellationToken cancellationToken)
    => Task.FromResult(RequestResponse<SynchronizationStatus>.Ok(provider.Status));
}
