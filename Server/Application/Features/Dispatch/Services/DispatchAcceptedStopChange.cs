using Application.Features.Execution.Models;
using Application.Features.Execution.Services;
using Domain.Entities.Dispatch;
using Domain.Entities.Execution;
using Domain.Models.Execution;

namespace Application.Features.Dispatch.Services;

public static class DispatchAcceptedStopChange
{
  public static async Task ApplyAsync(
    IAppDbContext db,
    ExecutionLeg leg,
    DispatchStop stop,
    string operation,
    Guid actor,
    DateTime now,
    CancellationToken ct
  )
  {
    var stops = ExecutionStopRows.Read(leg);
    var index = stops.FindIndex(x => x.Id == stop.Id);
    stops[index] = stop;
    var previous = leg.Status;
    if (leg.Status == "completed" && !stop.IsCompleted)
    {
      if (leg.EndSwitchId.HasValue)
        throw new DbUpdateConcurrencyException(
          "Use the transfer workflow to correct a released assignment."
        );
      leg.Status = "active";
      leg.CompletedAt = null;
    }
    var transfers = await ExecutionTransfers.ReadAsync(db, [leg], ct);
    ExecutionLegProgress.Apply(
      leg,
      stops,
      transfers
        .Values.Where(x => x.ConfirmedBy.HasValue)
        .Select(x => x.Id)
        .ToHashSet()
    );
    if (
      previous != "active"
      && leg.Status == "active"
      && await ExecutionResources.ConflictsAsync(
        db,
        new(leg.TruckId, leg.DriverId, leg.TrailerId, leg.CoDriverId),
        null,
        ct,
        leg.Id
      )
    )
      throw new DbUpdateConcurrencyException(
        "A resource is already active or awaiting a transfer receipt."
      );
    await ExecutionAcceptance.ApplyAsync(
      db,
      [new(leg, stops)],
      operation,
      actor,
      null,
      now,
      ct
    );
  }
}
