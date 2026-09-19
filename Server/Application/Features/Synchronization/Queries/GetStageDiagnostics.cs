using Application.Diagnostics;
using Application.Models;

namespace Application.Features.Synchronization.Queries;

public sealed record GetStageDiagnosticsQuery
  : IRequest<
    RequestResponse<IReadOnlyDictionary<string, PerformanceStages.StageTiming>>
  >;

public sealed class GetStageDiagnosticsHandler
  : IRequestHandler<
    GetStageDiagnosticsQuery,
    RequestResponse<IReadOnlyDictionary<string, PerformanceStages.StageTiming>>
  >
{
  public Task<
    RequestResponse<IReadOnlyDictionary<string, PerformanceStages.StageTiming>>
  > Handle(GetStageDiagnosticsQuery request, CancellationToken ct) =>
    Task.FromResult(
      RequestResponse<
        IReadOnlyDictionary<string, PerformanceStages.StageTiming>
      >.Ok(PerformanceStages.Snapshot())
    );
}
