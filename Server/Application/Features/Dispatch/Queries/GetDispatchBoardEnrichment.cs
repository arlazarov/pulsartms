using Application.Features.Dispatch.Models;
using Application.Models;

namespace Application.Features.Dispatch.Queries;

public sealed record GetDispatchBoardEnrichmentQuery(
  GetDispatchBoardQuery Board
) : IRequest<RequestResponse<IReadOnlyList<TruckDispatchEnrichment>>>;

public sealed class GetDispatchBoardEnrichmentHandler(ISender mediator)
  : IRequestHandler<
    GetDispatchBoardEnrichmentQuery,
    RequestResponse<IReadOnlyList<TruckDispatchEnrichment>>
  >
{
  public async Task<
    RequestResponse<IReadOnlyList<TruckDispatchEnrichment>>
  > Handle(GetDispatchBoardEnrichmentQuery request, CancellationToken ct)
  {
    var financials = request.Board.IncludeFinancials;
    var board = await mediator.Send(
      request.Board with
      {
        IncludeHos = false,
        IncludeEta = !financials,
        IdentitiesOnly = false,
      },
      ct
    );
    return board.Success && board.Response is { } result
      ? RequestResponse<IReadOnlyList<TruckDispatchEnrichment>>.Ok(
        result
          .Items.Select(row => TruckDispatchEnrichment.From(row, financials))
          .ToArray()
      )
      : RequestResponse<IReadOnlyList<TruckDispatchEnrichment>>.Fail(
        board.Errors ?? new(),
        board.StatusCode
      );
  }
}
