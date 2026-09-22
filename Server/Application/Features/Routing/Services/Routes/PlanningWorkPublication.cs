using Application.Caching;
using Application.Features.Execution.Services;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Services.Deadheads;
using Domain.Models.Execution;
using Domain.Models.Routing;
using Domain.Rules;
using Microsoft.EntityFrameworkCore.Storage;

namespace Application.Features.Routing.Services.Routes;

public sealed class PlanningWorkPublication(
  TruckItineraryReader itineraries,
  IPlanningPublicationScope scope,
  DeadheadHistoryService history,
  PlanningSummaryCache summaries,
  ICurrentCompany company,
  ReadCache reads
)
{
  private readonly Dictionary<
    PlanningSummaryCache.Key,
    PlanningSummaryCache.Work
  > committed = [];

  public PlanningSummaryCache.Work Current(
    PlanningSummaryCache.Work captured
  ) => committed.GetValueOrDefault(captured.Key, captured);

  public async Task CommitAsync(
    IDbContextTransaction transaction,
    Guid? truck,
    CancellationToken ct,
    Action? invalidate = null
  )
  {
    await transaction.CommitAsync(ct);
    invalidate?.Invoke();
    if (truck is { } changedTruck)
      reads.InvalidateItem("planning-inputs", changedTruck);
    if (company.Id is { } owner && truck is { } truckId)
      foreach (var work in summaries.Committed(owner, truckId))
        committed[work.Key] = work;
  }

  public Task<IDbContextTransaction> BeginAsync(
    TruckItinerarySnapshot expected,
    CancellationToken ct
  ) => BeginAsync(expected, [], ct);

  public async Task<IDbContextTransaction> BeginAsync(
    TruckItinerarySnapshot expected,
    IReadOnlyCollection<DeadheadHistoryBatch> historicalInputs,
    CancellationToken ct
  )
  {
    var transaction = await scope.BeginAsync(expected.TruckId, ct);
    try
    {
      var current = await itineraries.ReadAsync(
        expected.TruckId,
        expected.AsOf,
        ct
      );
      if (current?.InputSignature != expected.InputSignature)
        throw new RoutePlanningException(
          "The truck work changed. Refresh and calculate the plan again."
        );
      await history.RequirePredecessorsAsync(historicalInputs, ct);
      return transaction;
    }
    catch
    {
      await transaction.DisposeAsync();
      throw;
    }
  }
}
