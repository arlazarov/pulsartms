using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Queries;
using Application.Models;

namespace Application.Features.Dispatch.Interfaces;

// Board rows with dispatch details, HOS and financials. ETA enrichment lives in
// DispatchBoardService so ETA services can read the board without a dependency cycle;
// IncludeEta is not honored here.
public interface IDispatchBoardReader
{
  Task<PaginatedList<TruckDispatchBoardResponse>> ReadAsync(GetDispatchBoardQuery request, CancellationToken cancellationToken);
}
