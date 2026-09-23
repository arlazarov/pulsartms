using Application.Features.Routing.Services.Routes;
using Application.Interfaces;
using Domain.Models.Routing;
using Domain.Rules;
using Domain.Rules.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Application.Features.Routing.Background;

public interface IPlanningSummaryOperation : IBackgroundOperation;

public sealed class PlanningSummaryOperation(
  PlanningSummaryCache cache,
  IServiceScopeFactory scopes,
  TimeProvider time,
  ILogger<PlanningSummaryOperation> logger
) : IPlanningSummaryOperation
{
  // At most half the summary entries are held for running work, so trucks
  // somebody opens are never crowded out by the ones nobody has.
  public const int RunningWorkLimit = 128;

  public Task RunAsync(CancellationToken ct) =>
    Task.WhenAll(
      Enumerable
        .Range(0, 2)
        .Select(_ => ConsumeAsync(ct))
        .Append(KeepRunningWorkAsync(ct))
    );

  // Running work gets its summary whether or not anyone is looking. On the
  // same thirty-second cadence as the freshness path, per carrier, the
  // trucks with running work are read in one batch and asked for exactly as
  // a reader would; the consumers below prepare them. Nothing here
  // calculates: a road or a fuel plan is rebuilt only when its inputs
  // changed, as it is for any reader, and a click then only opens what is
  // already prepared.
  private async Task KeepRunningWorkAsync(CancellationToken ct)
  {
    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30), time);
    do
    {
      try
      {
        await using var scope = scopes.CreateAsyncScope();
        await CompanyPasses.ForEachCompanyAsync(
          scope.ServiceProvider,
          KeepRunningWorkOnceAsync,
          ct
        );
      }
      catch (OperationCanceledException) when (ct.IsCancellationRequested)
      {
        break;
      }
      catch (Exception ex)
      {
        logger.LogWarning(ex, "Running work summary demand failed");
      }
    } while (await timer.WaitForNextTickAsync(ct));
  }

  public async Task KeepRunningWorkOnceAsync(CancellationToken ct)
  {
    await using var scope = scopes.CreateAsyncScope();
    var services = scope.ServiceProvider;
    if (services.GetRequiredService<ICurrentCompany>().Id is not { } company)
      return;
    var inputs = services.GetRequiredService<TruckPlanningInputsReader>();
    var summaries = services.GetRequiredService<PlanningSummaryReader>();
    var trucks = await inputs.RunningTruckIdsAsync(RunningWorkLimit, ct);
    if (trucks.Count == 0)
      return;
    foreach (
      var (truck, work) in await inputs.ReadManyAsync(
        trucks,
        ct,
        includeHos: false
      )
    )
      cache.Keep(new(company, truck), summaries.Signature(work));
  }

  // Planning refused the work as it stands - it needs review, it has two
  // truck assignments. That is the summary's answer for these inputs, said
  // as it is, not "updating" forever. It is kept only if the inputs are
  // still the ones it was refused for: a refusal because the work moved
  // under it is not an answer, and the work is read again.
  private async Task<(string?, AutomaticPlanningResult?)> RefusedAsync(
    IServiceProvider services,
    PlanningSummaryCache.Work work,
    TruckPlanningInputs? captured,
    string? capturedSignature,
    string reason,
    CancellationToken ct
  )
  {
    if (captured is null || capturedSignature is null)
      return (null, null);
    using var owner = services
      .GetRequiredService<ICurrentCompany>()
      .As(work.Key.Company);
    var inputs = services.GetRequiredService<TruckPlanningInputsReader>();
    var summaries = services.GetRequiredService<PlanningSummaryReader>();
    TruckPlanningInputs? now;
    try
    {
      now = await inputs.ReadFreshAsync(work.Key.Truck, ct, includeHos: false);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      logger.LogWarning(
        ex,
        "Planning summary refusal check failed for truck {TruckId}",
        work.Key.Truck
      );
      return (null, null);
    }
    if (now is null || summaries.Signature(now) != capturedSignature)
      return (now is null ? null : summaries.Signature(now), null);
    var first = PlanningWorkPolicy
      .Candidates(captured.Itinerary)
      .FirstOrDefault(x =>
        work.Key.Dispatch is not { } dispatch || x.Work.DispatchId == dispatch
      );
    return (
      capturedSignature,
      new AutomaticPlanningResult(
        work.Key.Truck,
        first?.Work.DispatchId ?? work.Key.Dispatch,
        first?.LoadNumber,
        null,
        reason
      )
      {
        ExecutionLegId = first?.Work.ExecutionLegId,
        AssignmentRevision = first?.AssignmentRevision ?? 0,
        CalculatedAt = time.GetUtcNow(),
      }
    );
  }

  private async Task ConsumeAsync(CancellationToken ct)
  {
    while (!ct.IsCancellationRequested)
    {
      var work = cache.Take();
      if (work is null)
      {
        await Task.Delay(TimeSpan.FromSeconds(1), time, ct);
        continue;
      }
      AutomaticPlanningResult? result = null;
      string? signature = null;
      await using var scope = scopes.CreateAsyncScope();
      var services = scope.ServiceProvider;
      TruckPlanningInputs? captured = null;
      string? capturedSignature = null;
      try
      {
        using var owner = services
          .GetRequiredService<ICurrentCompany>()
          .As(work.Key.Company);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        var token = timeout.Token;
        var inputs = services.GetRequiredService<TruckPlanningInputsReader>();
        captured = await inputs.ReadFreshAsync(
          work.Key.Truck,
          token,
          includeHos: true
        );
        if (captured is null)
          continue;
        var summaries = services.GetRequiredService<PlanningSummaryReader>();
        capturedSignature = summaries.Signature(captured);
        var reader = services.GetRequiredService<PlanningReadService>();
        var candidate = work.Key.Dispatch is { } dispatch
          ? await reader.ForDispatchAsync(dispatch, token)
          : await reader.ForTruckAsync(work.Key.Truck, token);
        await inputs.RequireCurrentAsync(captured.Itinerary, token);
        signature = summaries.Signature(captured);
        if (signature != capturedSignature)
          continue;
        if (candidate.State?.Plan is { } plan)
          PlanningReadService.TrimForDisplay(plan);
        result = candidate with { CalculatedAt = time.GetUtcNow() };
      }
      catch (OperationCanceledException) when (ct.IsCancellationRequested)
      {
        break;
      }
      catch (RoutePlanningException ex)
      {
        (signature, result) = await RefusedAsync(
          services,
          work,
          captured,
          capturedSignature,
          ex.Message,
          ct
        );
      }
      catch (OperationCanceledException) { }
      catch (Exception ex)
      {
        logger.LogWarning(
          ex,
          "Planning summary refresh failed for truck {TruckId}",
          work.Key.Truck
        );
      }
      finally
      {
        cache.Complete(work, signature, result);
      }
    }
  }
}
