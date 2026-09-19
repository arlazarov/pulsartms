using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application.Features.Routing.Background;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Models;
using Application.Features.Synchronization.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Application.Features.Routing.Services.Routes;

public sealed class PlanningRefreshQueue(
  IPlanningRefreshStore store,
  PlanningRefreshSignal signal,
  IMemoryCache cache,
  IOptions<SynchronizationOptions> options,
  TimeProvider time
)
{
  private sealed record Memo(string Signature, PlanningRefreshState State);

  public static string ErrorKey(
    Guid id,
    RoutePlanningState state,
    string inputIdentity,
    Guid? executionLegId = null,
    long assignmentRevision = 0
  ) =>
    $"automatic-planning-error:{id}:{InputSignature(state, inputIdentity)}"
    + ScopeSuffix(
      executionLegId ?? state.Plan?.ExecutionLegId,
      executionLegId.HasValue
        ? assignmentRevision
        : state.Plan?.AssignmentRevision ?? 0
    );

  public string? Message(
    Guid id,
    RoutePlanningState state,
    string inputIdentity,
    Guid? executionLegId = null,
    long assignmentRevision = 0,
    bool pending = false
  )
  {
    var key = ErrorKey(
      id,
      state,
      inputIdentity,
      executionLegId,
      assignmentRevision
    );
    if (cache.TryGetValue<string>(key, out var error))
      return error;
    if (state.Plan is { InputsChanged: false })
      return null;
    return pending ? "Route update queued." : "Route update pending.";
  }

  public async Task<PlanningRefreshState?> EnqueueAsync(
    PlanningScope scope,
    RoutePlanningState state,
    string inputIdentity,
    CancellationToken ct
  )
  {
    var now = time.GetUtcNow().UtcDateTime;
    if (state.Plan is { InputsChanged: false } plan)
    {
      var age = now - plan.CalculatedAt;
      if (
        age >= TimeSpan.Zero
        && age < TimeSpan.FromSeconds(options.Value.OnDemandPlanningSeconds)
      )
        return null;
    }
    var signature = InputSignature(state, inputIdentity);
    var key =
      $"planning-demand:{scope.DispatchId}"
      + ScopeSuffix(scope.ExecutionLegId, scope.AssignmentRevision);
    if (
      cache.TryGetValue<Memo>(key, out var memo)
      && memo!.Signature == signature
    )
      return memo.State;
    var accepted = await store.RequestAsync(scope, signature, now, ct);
    cache.Set(key, new Memo(signature, accepted), TimeSpan.FromSeconds(5));
    if (accepted.Pending)
      signal.Pulse();
    return accepted;
  }

  private static string ScopeSuffix(Guid? legId, long revision) =>
    legId.HasValue ? $":leg:{legId}:assignment:{revision}" : "";

  private static string InputSignature(
    RoutePlanningState state,
    string inputIdentity
  ) =>
    Convert.ToHexString(
      SHA256.HashData(
        Encoding.UTF8.GetBytes(
          $"{inputIdentity}:{ProfileSignature(state.Profile)}"
            + $":{state.RouteChoiceRevision}"
        )
      )
    );

  private static string ProfileSignature(TruckRouteProfile profile) =>
    Convert.ToHexString(
      SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(profile, RoutePlanningService.Json)
      )
    );
}
