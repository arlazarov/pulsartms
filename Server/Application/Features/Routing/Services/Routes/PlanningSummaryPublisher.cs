using System.Text.Json;
using Domain.Models.Routing;
using Domain.Rules;

namespace Application.Features.Routing.Services.Routes;

public sealed class PlanningSummaryPublisher(
  PlanningSummaryCache cache,
  TruckPlanningInputsReader inputs,
  PlanningSummaryReader summaries,
  PlanningWorkPublication publication,
  PlanningReadService reader,
  TimeProvider time
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
    await inputs.RequireCurrentAsync(inputsNow.Itinerary, ct);
    if (summaries.Signature(inputsNow) != signature)
      return;
    PlanningReadService.TrimForDisplay(snapshot.State!.Plan!);
    snapshot = snapshot with { CalculatedAt = time.GetUtcNow() };
    foreach (var target in targets)
      cache.Complete(target, signature, snapshot);
  }
}
