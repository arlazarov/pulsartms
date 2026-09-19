using Application.Features.Execution.Models;
using Application.Features.Execution.Services;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services.Deadheads;
using Microsoft.EntityFrameworkCore.Storage;

namespace Application.Features.Routing.Services.Routes;

public sealed class PlanningWorkPublication(
  TruckItineraryReader itineraries,
  IPlanningPublicationScope scope,
  DeadheadHistoryService history
)
{
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
