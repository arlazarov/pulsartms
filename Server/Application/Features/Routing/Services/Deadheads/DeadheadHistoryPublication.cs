using Application.Caching;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Services.FuelPlanning;
using Application.Features.Routing.Services.Routes;
using Domain.Models.Routing;
using Domain.Rules;
using Microsoft.EntityFrameworkCore.Storage;

namespace Application.Features.Routing.Services.Deadheads;

public sealed class DeadheadHistoryPublication(
  DeadheadHistoryService history,
  IPlanningPublicationScope scope,
  PlanningSummaryCache summaries,
  ICurrentCompany company,
  ReadCache reads
)
{
  // Commits a connection, then tells the truck's planning summary: the
  // fuel plan it shows was checked against the saved connections
  // (FuelSavedInputsValidation), so a new one is a changed dependency,
  // prepared again once rather than waited out (stage 4d).
  public async Task CommitAsync(
    IDbContextTransaction transaction,
    Guid? truck,
    CancellationToken ct
  )
  {
    await transaction.CommitAsync(ct);
    if (truck is { } changed)
      reads.InvalidateItem(
        FuelSavedInputsValidation.SavedInputsFamily,
        changed
      );
    if (company.Id is { } owner && truck is { } id)
      summaries.Committed(owner, id);
  }

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
