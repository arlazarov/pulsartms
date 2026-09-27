using Application.Caching;
using Application.Features.Eta.Services;
using Domain.Models.Execution;
using Domain.Models.Routing;
using Domain.Rules.Routing;

namespace Application.Features.Routing.Services.Routes;

public sealed class PlanningSummaryReader(
  PlanningSummaryCache cache,
  TruckPlanningInputsReader inputs,
  RoutePlanningService routes,
  ICurrentCompany company,
  ReadCache reads,
  EtaService eta
)
{
  // The work a summary without its own result speaks for: the dispatch it
  // was asked about, or else the truck's current work as its inputs chose
  // it - never simply the first candidate, which may be work planning has
  // already moved past.
  internal static TruckWorkSegment? Scope(
    TruckPlanningInputs work,
    Guid? dispatch
  ) =>
    dispatch is null
      ? work.CurrentSegment
      : PlanningWorkPolicy
        .Candidates(work.Itinerary)
        .FirstOrDefault(x => x.Work.DispatchId == dispatch);

  // What a summary was prepared for: the truck's work, the current work
  // the inputs chose at its revision, and the settings. The itinerary alone
  // does not change when tracking passes a load, so a summary prepared for
  // the passed load stayed "current" until its next refresh on any process
  // the commit did not reach (stage 4a); naming the choice here retires it
  // as soon as the inputs move on.
  public string Signature(TruckPlanningInputs work) =>
    $"{work.Itinerary.InputSignature}"
    + $":{work.CurrentWork?.DispatchId:N}/{work.CurrentWork?.ExecutionLegId:N}"
    + $"/{work.CurrentAssignmentRevision}"
    + $":{reads.Generation(ReadGroups.Settings)}";

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
      var first = Scope(work, dispatch);
      return new(
        truck,
        first?.Work.DispatchId,
        first?.LoadNumber,
        null,
        "Planning summary is updating."
      )
      {
        Hos = work.Hos,
        WorkConflicts = WorkPlacements.Conflicts(work),
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
      // The forecast as it stands now, not as it stood when the summary
      // was prepared up to half a minute ago: read from memory without
      // side effects, against the prepared plan's own identity. Without a
      // plan there is no identity to read against; the prepared answer
      // stands.
      State = result.State is { Plan: not null } state
        ? state with
        {
          Eta = eta.PeekForDisplay(state),
        }
        : result.State,
      WorkConflicts = WorkPlacements.Conflicts(work),
      Message =
        result.IsRefreshing && result.Message is null
          ? "Planning summary is updating."
          : result.Message,
    };
  }

  // summaryOnly: the result without route geometry, for a reader that
  // needs a scalar of it and not the road.
  public async Task<AutomaticPlanningResult> ForTruckAsync(
    Guid truckId,
    CancellationToken ct,
    Guid? knownPlanId = null,
    int? knownVersion = null,
    bool summaryOnly = false
  )
  {
    var work = await inputs.ReadAsync(truckId, ct);
    return work is null
      ? new(truckId, null, null, null, "No remaining dispatches.")
      : Read(
        work,
        knownPlanId: knownPlanId,
        knownVersion: knownVersion,
        summaryOnly: summaryOnly
      );
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
    var current = work.CurrentSegment;
    return Read(
      work,
      current?.Work.DispatchId == dispatchId ? null : dispatchId,
      knownPlanId,
      knownVersion
    );
  }
}
