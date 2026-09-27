using Application.Features.Dispatch.Queries;
using Domain.Models.Routing;

namespace Application.Features.Routing.Services.Routes;

public sealed class BoardPlanningReader(
  PlanningReadService planning,
  PlanningSummaryReader summaries
)
{
  public async Task<List<AutomaticPlanningResult>> ReadAsync(
    GetDispatchBoardQuery query,
    CancellationToken ct
  ) =>
    [
      .. await summaries.ReadManyAsync(
        await planning.ReadBoardInputsAsync(query, ct),
        ct,
        summaryOnly: true
      ),
    ];
}
