using Application.Behaviors;
using Application.Models;

namespace Application.Features.Synchronization.Queries;

public sealed record GetRequestDiagnosticsQuery : IRequest<RequestResponse<IReadOnlyDictionary<string, RequestTiming>>>;

public sealed class GetRequestDiagnosticsHandler : IRequestHandler<GetRequestDiagnosticsQuery, RequestResponse<IReadOnlyDictionary<string, RequestTiming>>>
{
  public Task<RequestResponse<IReadOnlyDictionary<string, RequestTiming>>> Handle(GetRequestDiagnosticsQuery request, CancellationToken cancellationToken)
    => Task.FromResult(RequestResponse<IReadOnlyDictionary<string, RequestTiming>>.Ok(RequestMetrics.Snapshot()));
}
