using Application.Behaviors;
using Application.Models;

namespace Application.Features.Synchronization.Queries;

public sealed record GetRequestDiagnosticsQuery : IRequest<RequestResponse<IReadOnlyDictionary<string, RequestMetrics.RequestTiming>>>;

public sealed class GetRequestDiagnosticsHandler : IRequestHandler<GetRequestDiagnosticsQuery, RequestResponse<IReadOnlyDictionary<string, RequestMetrics.RequestTiming>>>
{
  public Task<RequestResponse<IReadOnlyDictionary<string, RequestMetrics.RequestTiming>>> Handle(GetRequestDiagnosticsQuery request, CancellationToken cancellationToken)
    => Task.FromResult(RequestResponse<IReadOnlyDictionary<string, RequestMetrics.RequestTiming>>.Ok(RequestMetrics.Snapshot()));
}
