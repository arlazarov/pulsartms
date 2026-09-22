using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Services.Routes;
using Application.Models;
using Domain.Models.Routing;

namespace Application.Features.Dispatch.Queries;

public sealed record GetDispatchPlanningSummariesQuery(
  int Page = 1,
  string? Search = null,
  Guid? TruckId = null,
  DateOnly? Date = null
)
  : IRequest<RequestResponse<List<AutomaticPlanningResult>>>,
    IPlanningRequest,
    IChecked
{
  public IEnumerable<string> Wrong()
  {
    if (Page is < 1 or > 1000000)
      yield return "Choose a page from 1 to 1000000.";
    if (Search?.Length > 200)
      yield return "That search is too long.";
  }
}

public sealed class GetDispatchPlanningSummariesHandler(
  BoardPlanningReader planning
)
  : IRequestHandler<
    GetDispatchPlanningSummariesQuery,
    RequestResponse<List<AutomaticPlanningResult>>
  >
{
  public async Task<RequestResponse<List<AutomaticPlanningResult>>> Handle(
    GetDispatchPlanningSummariesQuery request,
    CancellationToken ct
  ) =>
    RequestResponse<List<AutomaticPlanningResult>>.Ok(
      await planning.ReadAsync(
        new(
          Page: request.Page,
          Search: request.Search,
          TruckId: request.TruckId,
          Date: request.Date
        ),
        ct
      )
    );
}
