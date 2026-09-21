using Application.Features.Execution.Models;
using Domain.Entities.Dispatch;
using Domain.Entities.Execution;
using Domain.Models.Execution;
using DispatchEntity = Domain.Entities.Dispatch.Dispatch;

namespace Application.Features.Execution.Services;

public static class InitialExecutionAssignment
{
  public static async Task<(
    ExecutionLeg? Leg,
    string? ReviewReason
  )> AcceptAsync(
    IAppDbContext db,
    DispatchEntity load,
    IReadOnlyList<DispatchStop> stops,
    Guid? actor,
    Guid? correlationId,
    DateTime now,
    CancellationToken ct,
    string? sourceAssignmentSignature = null
  )
  {
    if (db.Database.CurrentTransaction is null)
      throw new InvalidOperationException(
        "Initial execution requires the owning assignment transaction."
      );
    if (await db.LoadExecutionLegs.AnyAsync(x => x.DispatchId == load.Id, ct))
      throw new DbUpdateConcurrencyException(
        "This load already has accepted execution."
      );
    var inputs = InitialExecutionInputs.Resolve(load, stops, now);
    if (inputs.ReviewReason is not null)
      return (null, inputs.ReviewReason);
    stops = inputs.Stops;
    var truck = stops[0].TruckId!.Value;
    var first = stops[0];
    var leg = new ExecutionLeg
    {
      Id = Guid.NewGuid(),
      Trip = new()
      {
        Id = Guid.NewGuid(),
        Name = $"Load {load.LoadNumber}",
        Revision = 1,
        RecordedAt = now,
        RecordedBy = actor,
      },
      TruckId = truck,
      DriverId = first.DriverId,
      CoDriverId = first.CoDriverId,
      TrailerId = first.TrailerId,
      Revision = 1,
      SourceAssignmentSignature =
        sourceAssignmentSignature
        ?? await db
          .DispatchSourceLinks.Where(x => x.DispatchId == load.Id)
          .Select(x => x.AssignmentSignature)
          .SingleOrDefaultAsync(ct)
        ?? "",
      SourceSignature = ExecutionSnapshots.Fingerprint(load),
      RecordedAt = now,
      RecordedBy = actor,
    };
    leg.TripId = leg.Trip.Id;
    leg.SourceObservedSignature = leg.SourceSignature;
    ExecutionLegProgress.Apply(leg, stops);
    if (
      leg.Status == "active"
      && await ExecutionResources.ConflictsAsync(
        db,
        new(truck, first.DriverId, first.TrailerId, first.CoDriverId),
        null,
        ct
      )
    )
      throw new DbUpdateConcurrencyException(
        "A resource is already active or awaiting a transfer receipt."
      );
    leg.Trip.Status = leg.Status;
    leg.Loads.Add(
      new()
      {
        Id = Guid.NewGuid(),
        DispatchId = load.Id,
        ExecutionLegId = leg.Id,
        Sequence = 1,
        StartVisitId = stops[0].Id,
        EndVisitId = stops[^1].Id,
      }
    );
    db.ExecutionLegs.Add(leg);
    await ExecutionAcceptance.ApplyAsync(
      db,
      [new(leg, stops)],
      actor.HasValue ? "assignment-accepted" : "source-assignment-accepted",
      actor,
      correlationId,
      now,
      ct
    );
    return (leg, null);
  }
}
