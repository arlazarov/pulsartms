using System.Data;
using Application.Features.Mileage.Models;
using Application.Features.Mileage.Services;
using Application.Models;
using Domain.Entities.Mileage;

namespace Application.Features.Mileage.Commands;

public sealed record UpdateMovementDistanceCommand(
  Guid MovementId,
  MovementDistanceUpdate Update
) : IRequest<RequestResponse<MileageMovementRow>>;

public sealed class UpdateMovementDistanceHandler(
  IAppDbContext db,
  ICurrentUser caller,
  IUserRoleService roles,
  TimeProvider clock
)
  : IRequestHandler<
    UpdateMovementDistanceCommand,
    RequestResponse<MileageMovementRow>
  >
{
  public async Task<RequestResponse<MileageMovementRow>> Handle(
    UpdateMovementDistanceCommand command,
    CancellationToken ct
  )
  {
    try
    {
      return await UpdateAsync(command, ct);
    }
    catch (Exception exception) when (db.IsWriteConflict(exception))
    {
      return MileageMutation.Conflict();
    }
  }

  private async Task<RequestResponse<MileageMovementRow>> UpdateAsync(
    UpdateMovementDistanceCommand command,
    CancellationToken ct
  )
  {
    var actor = await MileageAccess.ActorAsync(db, caller, roles, false, ct);
    if (actor is null)
      return RequestResponse<MileageMovementRow>.Fail("Access denied.", 403);
    var update = command.Update;
    var now = clock.GetUtcNow();
    if (
      update is null
      || update.Revision is < 1 or long.MaxValue
      || update.Miles is < 0 or > 1_000_000
      || decimal.Round(update.Miles, 3) != update.Miles
      || update.Basis is not ("planned" or "actual")
      || update.ObservedAt > now
      || update.ObservedAt.Year < 2000
      || string.IsNullOrWhiteSpace(update.SourceReference)
      || update.SourceReference.Length > 300
      || string.IsNullOrWhiteSpace(update.Reason)
      || update.Reason.Length > 500
      || (
        update.Basis == "planned"
          ? update.Source != "manual-estimate"
          : update.Source is not ("manual-odometer" or "manual-distance-record")
      )
    )
      return RequestResponse<MileageMovementRow>.Fail(
        "Provide valid miles, basis, evidence source, timestamp and reason.",
        400
      );
    await using var transaction = await db.Database.BeginTransactionAsync(
      IsolationLevel.Serializable,
      ct
    );
    var movement = await db.Movements.SingleOrDefaultAsync(
      x => x.Id == command.MovementId,
      ct
    );
    if (movement is null)
      return RequestResponse<MileageMovementRow>.Fail(
        "Movement not found.",
        404
      );
    if (movement.Revision != update.Revision)
      return MileageMutation.Conflict();
    if (movement.Origin != "manual")
      return RequestResponse<MileageMovementRow>.Fail(
        "Automatic source evidence cannot be replaced by a manual distance.",
        409
      );
    var start = update.StartedAt?.UtcDateTime ?? movement.StartedAt;
    var end = update.EndedAt?.UtcDateTime ?? movement.EndedAt;
    if (
      update.Basis == "planned"
      && (update.StartedAt.HasValue || update.EndedAt.HasValue)
    )
      return RequestResponse<MileageMovementRow>.Fail(
        "Planned estimates cannot record an actual movement interval.",
        400
      );
    if (
      update.Basis == "actual"
      && (
        !MovementIntervals.ValidActual(
          start,
          end,
          update.ObservedAt.UtcDateTime,
          now.UtcDateTime
        )
        || movement.StartedAt.HasValue && movement.StartedAt != start
        || movement.EndedAt.HasValue && movement.EndedAt != end
      )
    )
      return RequestResponse<MileageMovementRow>.Fail(
        "Actual evidence must cover the movement's confirmed start and end.",
        400
      );
    if (update.Basis == "actual")
    {
      if (movement.ExecutionLegId is { } legId)
      {
        var leg = await db
          .ExecutionLegs.AsNoTracking()
          .SingleOrDefaultAsync(x => x.Id == legId, ct);
        if (
          leg is null
          || leg.StartedAt is null
          || start < leg.StartedAt
          || leg.CompletedAt.HasValue && end > leg.CompletedAt
          || leg.TruckId != movement.TruckId
          || leg.DriverId != movement.DriverId
          || leg.CoDriverId != movement.CoDriverId
          || leg.TrailerId != movement.TrailerId
          || !await db.LockExecutionLegAsync(legId, leg.Revision, ct)
        )
          return RequestResponse<MileageMovementRow>.Fail(
            "Actual movement does not match the confirmed execution.",
            409
          );
      }
      if (
        await db.Movements.AnyAsync(
          MovementIntervals.OverlappingTruck(
            movement.TruckId,
            start,
            end,
            movement.Id
          ),
          ct
        )
      )
        return RequestResponse<MileageMovementRow>.Fail(
          "The truck already has a movement in that time range.",
          409
        );
      movement.StartedAt = start;
      movement.EndedAt = end;
    }
    movement.Revision++;
    var evidence = new MovementDistanceEvidence
    {
      Id = Guid.NewGuid(),
      MovementId = movement.Id,
      Revision = movement.Revision,
      Basis = update.Basis,
      Miles = update.Miles,
      Source = update.Source,
      SourceReference = update.SourceReference.Trim(),
      Reason = update.Reason.Trim(),
      ObservedAt = update.ObservedAt.UtcDateTime,
      StartedAt = update.Basis == "actual" ? start : null,
      EndedAt = update.Basis == "actual" ? end : null,
      RecordedAt = now.UtcDateTime,
      RecordedBy = actor.Value,
    };
    db.MovementDistanceEvidence.Add(evidence);
    if (update.Basis == "actual")
    {
      movement.ActualMiles = evidence.Miles;
      movement.ActualEvidenceId = evidence.Id;
      movement.ActualAt = evidence.ObservedAt;
      movement.ActualSource = evidence.Source;
      movement.ActualSourceReference = evidence.SourceReference;
    }
    else
    {
      movement.PlannedMiles = evidence.Miles;
      movement.PlannedEvidenceId = evidence.Id;
      movement.PlannedAt = evidence.ObservedAt;
      movement.PlannedSource = evidence.Source;
      movement.PlannedSourceReference = evidence.SourceReference;
    }
    try
    {
      var response = await MileageMutation.SaveAsync(
        db,
        movement,
        evidence,
        ct
      );
      if (response.Success)
        await transaction.CommitAsync(ct);
      return response;
    }
    catch (Exception exception) when (db.IsWriteConflict(exception))
    {
      db.Entry(movement).State = EntityState.Detached;
      db.Entry(evidence).State = EntityState.Detached;
      return MileageMutation.Conflict();
    }
  }
}
