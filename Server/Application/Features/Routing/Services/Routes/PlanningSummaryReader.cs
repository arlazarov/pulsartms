using Application.Caching;
using Domain.Models.Routing;
using Domain.Rules.Routing;

namespace Application.Features.Routing.Services.Routes;

public sealed class PlanningSummaryReader(
  PlanningSummaryCache cache,
  TruckPlanningInputsReader inputs,
  RoutePlanningService routes,
  ICurrentCompany company,
  ReadCache reads
)
{
  public string Signature(TruckPlanningInputs work) =>
    $"{work.Itinerary.InputSignature}:{reads.Generation("settings")}";

  public AutomaticPlanningResult Read(
    TruckPlanningInputs work,
    Guid? dispatch = null,
    Guid? knownPlanId = null,
    int? knownVersion = null,
    bool summaryOnly = false
  )
  {
    var truck = work.Itinerary.TruckId;
    var result = cache.Read(
      new(
        company.Id
          ?? throw new InvalidOperationException("A company is required."),
        truck,
        dispatch
      ),
      Signature(work),
      !summaryOnly,
      knownPlanId,
      knownVersion
    );
    if (result is null)
    {
      var first = PlanningWorkPolicy
        .Candidates(work.Itinerary)
        .FirstOrDefault(x => dispatch is null || x.Work.DispatchId == dispatch);
      return new(
        truck,
        first?.Work.DispatchId,
        first?.LoadNumber,
        null,
        "Planning summary is updating."
      )
      {
        Hos = work.Hos,
        ExecutionLegId = first?.Work.ExecutionLegId,
        AssignmentRevision = first?.AssignmentRevision ?? 0,
        IsRefreshing = true,
      };
    }
    if (result.State?.Plan is { } plan)
      PlanningReadService.TrimForDisplay(
        plan,
        summaryOnly ? plan.Id : knownPlanId,
        summaryOnly ? plan.Version : knownVersion
      );
    return result with
    {
      Hos = work.Hos,
      Message =
        result.IsRefreshing && result.Message is null
          ? "Planning summary is updating."
          : result.Message,
    };
  }

  public async Task<AutomaticPlanningResult> ForTruckAsync(
    Guid truckId,
    CancellationToken ct,
    Guid? knownPlanId = null,
    int? knownVersion = null
  )
  {
    var work = await inputs.ReadAsync(truckId, ct);
    return work is null
      ? new(truckId, null, null, null, "No remaining dispatches.")
      : Read(work, knownPlanId: knownPlanId, knownVersion: knownVersion);
  }

  public async Task<AutomaticPlanningResult> ForDispatchAsync(
    Guid dispatchId,
    CancellationToken ct,
    Guid? knownPlanId = null,
    int? knownVersion = null
  )
  {
    var load = await routes.LoadAsync(dispatchId, ct);
    var work = await inputs.ReadAsync(load.TruckId!.Value, ct);
    if (work is null)
      return new(
        load.TruckId.Value,
        dispatchId,
        load.LoadNumber,
        null,
        "No remaining dispatches."
      );
    var current = PlanningWorkPolicy
      .Candidates(work.Itinerary)
      .FirstOrDefault();
    return Read(
      work,
      current?.Work.DispatchId == dispatchId ? null : dispatchId,
      knownPlanId,
      knownVersion
    );
  }
}
