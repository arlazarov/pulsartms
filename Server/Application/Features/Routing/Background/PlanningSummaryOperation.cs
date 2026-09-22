using Application.Features.Routing.Services.Routes;
using Application.Interfaces;
using Domain.Models.Routing;
using Domain.Rules;
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
  public Task RunAsync(CancellationToken ct) =>
    Task.WhenAll(Enumerable.Range(0, 2).Select(_ => ConsumeAsync(ct)));

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
      try
      {
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        using var owner = services
          .GetRequiredService<ICurrentCompany>()
          .As(work.Key.Company);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        var token = timeout.Token;
        var inputs = services.GetRequiredService<TruckPlanningInputsReader>();
        var captured = await inputs.ReadFreshAsync(
          work.Key.Truck,
          token,
          includeHos: true
        );
        if (captured is null)
          continue;
        var summaries = services.GetRequiredService<PlanningSummaryReader>();
        var capturedSignature = summaries.Signature(captured);
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
      catch (RoutePlanningException) { }
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
