using System.Text.Json;
using Application.Features.Eta.Services;
using Domain.Models.Routing;
using Domain.Rules;
using Microsoft.Extensions.Logging;

namespace Application.Features.Routing.Services.Routes;

public sealed class PlanningSummaryPublisher(
  PlanningSummaryCache cache,
  TruckPlanningInputsReader inputs,
  PlanningSummaryReader summaries,
  PlanningWorkPublication publication,
  PlanningReadService reader,
  TimeProvider time,
  EtaMemory etas,
  ILogger<PlanningSummaryPublisher> logger
)
{
  public async Task PublishAsync(
    IReadOnlyList<PlanningSummaryCache.Work> captured,
    AutomaticPlanningResult result,
    CancellationToken ct
  )
  {
    var targets = captured
      .Select(publication.Current)
      .Where(cache.IsCurrent)
      .ToArray();
    if (
      targets.Length == 0
      || result.State?.Plan is not { Tracking.AllStopsPassed: false }
    )
      return;
    var inputsNow = await inputs.ReadFreshAsync(
      result.TruckId,
      ct,
      includeHos: true
    );
    if (inputsNow is null)
      return;
    var signature = summaries.Signature(inputsNow);
    targets = targets.Where(x => x.Signature == signature).ToArray();
    if (targets.Length == 0)
      return;
    // Display trimming must not alter the calculation used by fuel refresh.
    var snapshot = JsonSerializer.Deserialize<AutomaticPlanningResult>(
      JsonSerializer.SerializeToUtf8Bytes(result, RoutingJson.Options),
      RoutingJson.Options
    )!;
    snapshot = await reader.PrepareDisplayAsync(snapshot, inputsNow, ct);
    NoteEta(snapshot);
    await inputs.RequireCurrentAsync(inputsNow.Itinerary, ct);
    if (summaries.Signature(inputsNow) != signature)
      return;
    PlanningReadService.TrimForDisplay(snapshot.State!.Plan!);
    snapshot = snapshot with { CalculatedAt = time.GetUtcNow() };
    foreach (var target in targets)
      cache.Complete(target, signature, snapshot);
  }

  // The open map ETA incident (docs/archive/2026-09/eta-11007-2026-09-25.md):
  // the map shows the summary's ETA, and trucks read a dash while their
  // forecasts were saved every half minute. One line when a truck's summary
  // starts or stops carrying an ETA, with what the ETA memory answered;
  // nothing while it stays the same, so polling is quiet.
  private void NoteEta(AutomaticPlanningResult snapshot)
  {
    if (
      snapshot.State?.Plan is { } plan
      && etas.SummaryAnswerChange(
        plan.DispatchId,
        plan.ExecutionLegId,
        snapshot.State.Eta is not null
      )
        is { } answer
    )
      logger.LogInformation(
        "Planning summary ETA for truck {TruckId} load {DispatchId} leg "
          + "{ExecutionLegId}: {EtaAnswer}",
        snapshot.TruckId,
        plan.DispatchId,
        plan.ExecutionLegId,
        answer
      );
  }
}
