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
  EtaForecastService forecasts
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

  // The prepared part of a summary, without its forecast: readers get the
  // whole through ReadAsync or ReadManyAsync.
  internal AutomaticPlanningResult Read(
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
      WorkConflicts = WorkPlacements.Conflicts(work),
      Message =
        result.IsRefreshing && result.Message is null
          ? "Planning summary is updating."
          : result.Message,
    };
  }

  public async Task<AutomaticPlanningResult> ReadAsync(
    TruckPlanningInputs work,
    CancellationToken ct,
    Guid? dispatch = null,
    Guid? knownPlanId = null,
    int? knownVersion = null,
    bool summaryOnly = false
  ) =>
    (
      await WithForecastsAsync(
        [Read(work, dispatch, knownPlanId, knownVersion, summaryOnly)],
        ct
      )
    )[0];

  // Many trucks' summaries, their forecasts read in one batch.
  public Task<IReadOnlyList<AutomaticPlanningResult>> ReadManyAsync(
    IEnumerable<TruckPlanningInputs> works,
    CancellationToken ct,
    bool summaryOnly = false
  ) =>
    WithForecastsAsync(
      [.. works.Select(work => Read(work, summaryOnly: summaryOnly))],
      ct
    );

  // The forecast as it stands now - the newest valid committed one, from
  // this process' memory or the saved forecasts - not as it stood when the
  // summary was prepared. Without a plan there is no identity to read
  // against, and the prepared answer stands.
  private async Task<IReadOnlyList<AutomaticPlanningResult>> WithForecastsAsync(
    List<AutomaticPlanningResult> results,
    CancellationToken ct
  )
  {
    var planned = results
      .Select((result, index) => (result.State, Index: index))
      .Where(x => x.State?.Plan is not null)
      .ToList();
    if (planned.Count == 0)
      return results;
    var etas = await forecasts.ReadForDisplayAsync(
      [.. planned.Select(x => x.State!)],
      ct
    );
    for (var i = 0; i < planned.Count; i++)
      results[planned[i].Index] = results[planned[i].Index] with
      {
        State = planned[i].State! with { Eta = etas[i] },
      };
    return results;
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
      : await ReadAsync(
        work,
        ct,
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
    return await ReadAsync(
      work,
      ct,
      current?.Work.DispatchId == dispatchId ? null : dispatchId,
      knownPlanId,
      knownVersion
    );
  }
}
