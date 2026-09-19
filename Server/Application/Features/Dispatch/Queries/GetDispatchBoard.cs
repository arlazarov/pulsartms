using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Services;
using Application.Models;

namespace Application.Features.Dispatch.Queries;

public record GetDispatchBoardQuery(int Page = 1, int PageSize = 12, string? Search = null,
  Guid? TruckId = null, DateOnly? Date = null, bool IncludeHos = true, bool IncludePlanned = false, bool IncludeFinancials = true, bool IncludeEta = true,
  bool IncludeOverdue = false)
  : IRequest<RequestResponse<PaginatedList<TruckDispatchBoardResponse>>>;

public class GetDispatchBoardValidator : AbstractValidator<GetDispatchBoardQuery>
{
  public GetDispatchBoardValidator()
  {
    RuleFor(x => x.Page).InclusiveBetween(1, 1000000);
    RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    RuleFor(x => x.Search).MaximumLength(200);
  }
}

public class GetDispatchBoardHandler(DispatchBoardService board)
  : IRequestHandler<GetDispatchBoardQuery, RequestResponse<PaginatedList<TruckDispatchBoardResponse>>>
{
  public async Task<RequestResponse<PaginatedList<TruckDispatchBoardResponse>>> Handle(
    GetDispatchBoardQuery request, CancellationToken cancellationToken) =>
    RequestResponse<PaginatedList<TruckDispatchBoardResponse>>.Ok(await board.ReadAsync(request, cancellationToken));
}
