using Application.Caching;
using Domain.Models.Eta;
using Domain.Models.Routing;

namespace Application.Features.Eta.Services;

// The forecast a display shows for a prepared plan: the newest committed
// forecast of that plan's work, whichever process committed it (stage 4e
// of docs/architecture/current-work.md). The store is the truth; this
// process' memory holds only forecasts it committed itself and is a faster
// copy of them, never an answer of its own. Both are judged by the one
// decision (EtaService.Decide) against the plan, and the later calculation
// of the same work wins. Of two made at the same instant the saved one
// wins: the store kept the first committed and refused the other, whose
// process may still hold it, so every process shows the same one. Saved forecasts are read in one batch for all the
// plans asked about, through the read cache per forecast scope, which
// drops a scope after each committed save here and on other processes
// through the cache relay: a warm read costs no query, a commit anywhere
// costs the next reader one, and a load that raced a commit is not kept.
public sealed partial class EtaForecastService
{
  public const string DisplayFamily = "eta-forecast";

  public async Task<IReadOnlyList<DispatchEta?>> ReadForDisplayAsync(
    IReadOnlyList<RoutePlanningState> states,
    CancellationToken ct
  )
  {
    var scopes = states
      .Select(x => x.Plan)
      .OfType<RoutePlan>()
      .GroupBy(Scope)
      .ToDictionary(x => x.Key, x => x.First().ExecutionLegId.HasValue);
    var saved =
      scopes.Count == 0
        ? new Dictionary<Guid, EtaForecastSnapshot>()
        : await reads.GetManyAsync<EtaForecastSnapshot>(
          DisplayFamily,
          scopes.Keys,
          "saved",
          async missing =>
          {
            var found = await ReadSavedAsync(
              [.. missing.Where(x => !scopes[x])],
              [.. missing.Where(x => scopes[x])],
              ct
            );
            return found.Values.ToDictionary(x =>
              x.ExecutionLegId ?? x.DispatchId
            );
          },
          ct
        );
    return
    [
      .. states.Select(state =>
        state.Plan is { } plan
          ? eta.ForDisplay(
            state,
            EtaService.Saved(saved.GetValueOrDefault(Scope(plan))),
            eta.Remembered(state)
          )
          : null
      ),
    ];
  }

  private static Guid Scope(RoutePlan plan) =>
    plan.ExecutionLegId ?? plan.DispatchId;

  // After a committed save: the next display read of these scopes, here or
  // on another process once the relay delivers it, reads them again.
  private void Committed(IEnumerable<EtaForecastSnapshot> snapshots)
  {
    foreach (var snapshot in snapshots)
      reads.InvalidateItem(
        DisplayFamily,
        snapshot.ExecutionLegId ?? snapshot.DispatchId
      );
  }
}
