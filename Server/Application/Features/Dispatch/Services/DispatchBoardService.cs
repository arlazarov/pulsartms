using Application.Features.Dispatch.Interfaces;
using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Queries;
using Application.Models;

namespace Application.Features.Dispatch.Services;

// The board as HTTP and planning reads see it: reader rows plus ETA forecasts when requested.
public sealed class DispatchBoardService(IDispatchBoardReader reader, Application.Features.Eta.Services.EtaForecastService eta)
{
  public async Task<PaginatedList<TruckDispatchBoardResponse>> ReadAsync(GetDispatchBoardQuery request, CancellationToken cancellationToken)
  {
    var page = await reader.ReadAsync(request, cancellationToken);
    if (request.IncludeEta)
      await eta.PopulateAsync(page.Items.SelectMany(x => x.Dispatches).DistinctBy(x => x.Id).ToArray(), cancellationToken, page.Items);
    return page;
  }
}
