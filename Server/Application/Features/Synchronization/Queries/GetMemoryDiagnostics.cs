using Application.Models;

namespace Application.Features.Synchronization.Queries;

public sealed record GetMemoryDiagnosticsQuery
  : IRequest<RequestResponse<MemoryDiagnostics>>;

public sealed class GetMemoryDiagnosticsHandler(
  IRuntimeMemoryReader runtime,
  IEnumerable<ICacheMemorySource> caches
)
  : IRequestHandler<
    GetMemoryDiagnosticsQuery,
    RequestResponse<MemoryDiagnostics>
  >
{
  public Task<RequestResponse<MemoryDiagnostics>> Handle(
    GetMemoryDiagnosticsQuery request,
    CancellationToken ct
  )
  {
    ct.ThrowIfCancellationRequested();
    return Task.FromResult(
      RequestResponse<MemoryDiagnostics>.Ok(
        new(
          runtime.Read(),
          caches.SelectMany(x => x.ReadMemory()).OrderBy(x => x.Name).ToArray()
        )
      )
    );
  }
}
