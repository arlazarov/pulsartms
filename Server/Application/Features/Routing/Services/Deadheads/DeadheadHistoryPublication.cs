using Application.Features.Routing.Interfaces;
using Domain.Models.Routing;
using Domain.Rules;
using Microsoft.EntityFrameworkCore.Storage;

namespace Application.Features.Routing.Services.Deadheads;

public sealed class DeadheadHistoryPublication(
  DeadheadHistoryService history,
  IPlanningPublicationScope scope
)
{
  public async Task<IDbContextTransaction> BeginAsync(
    DeadheadHistorySnapshot expected,
    CancellationToken ct
  )
  {
    var transaction = await scope.BeginAsync(expected.Current.TruckId, ct);
    try
    {
      var current = await history.ReadAsync(expected.Current, ct);
      if (current?.InputSignature != expected.InputSignature)
        throw new RoutePlanningException(
          "Historical truck work changed. Refresh the connection inputs."
        );
      return transaction;
    }
    catch
    {
      await transaction.DisposeAsync();
      throw;
    }
  }
}
