using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services.Routes;
using Application.Models;

namespace Application.Features.Dispatch.Queries;

public sealed record GetDispatchPlanningSummariesQuery(
  int Page = 1,
  string? Search = null,
  Guid? TruckId = null,
  DateOnly? Date = null
) : IRequest<RequestResponse<List<AutomaticPlanningResult>>>, IPlanningRequest;

public sealed class GetDispatchPlanningSummariesValidator
  : AbstractValidator<GetDispatchPlanningSummariesQuery>
{
  public GetDispatchPlanningSummariesValidator()
  {
    RuleFor(x => x.Page).InclusiveBetween(1, 1000000);
    RuleFor(x => x.Search).MaximumLength(200);
  }
}

public sealed class GetDispatchPlanningSummariesHandler(
  PlanningReadService planning
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
      await planning.ForBoardAsync(
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
