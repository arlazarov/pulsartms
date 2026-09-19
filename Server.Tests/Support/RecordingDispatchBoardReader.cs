using Application.Features.Dispatch.Interfaces;
using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Queries;
using Application.Models;

namespace Server.Tests.Support;

internal sealed class RecordingDispatchBoardReader(IDispatchBoardReader inner) : IDispatchBoardReader
{
  public List<GetDispatchBoardQuery> Requests { get; } = [];

  public Task<PaginatedList<TruckDispatchBoardResponse>> ReadAsync(GetDispatchBoardQuery request, CancellationToken cancellationToken)
  {
    Requests.Add(request);
    return inner.ReadAsync(request, cancellationToken);
  }
}
