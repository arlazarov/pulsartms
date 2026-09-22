using Application.Models;

namespace Application.Features.Synchronization.Queries;

public sealed record GetProcessMemoryMapQuery
  : IRequest<RequestResponse<ProcessMemoryMap>>;

public sealed class GetProcessMemoryMapHandler(IProcessMemoryMapReader reader)
  : IRequestHandler<GetProcessMemoryMapQuery, RequestResponse<ProcessMemoryMap>>
{
  public Task<RequestResponse<ProcessMemoryMap>> Handle(
    GetProcessMemoryMapQuery request,
    CancellationToken ct
  )
  {
    ct.ThrowIfCancellationRequested();
    return Task.FromResult(
      RequestResponse<ProcessMemoryMap>.Ok(reader.Read(ct))
    );
  }
}
