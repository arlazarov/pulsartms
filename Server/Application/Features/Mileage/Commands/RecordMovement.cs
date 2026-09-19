using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Application.Features.Mileage.Models;
using Application.Features.Mileage.Services;
using Application.Models;
using Domain.Entities.Mileage;

namespace Application.Features.Mileage.Commands;

public sealed record RecordMovementCommand(RecordMovementRequest Request)
  : IRequest<RequestResponse<MileageMovementRow>>;

public sealed class RecordMovementHandler(
  IAppDbContext db,
  ICurrentUser caller,
  IUserRoleService roles,
  TimeProvider clock
) : IRequestHandler<RecordMovementCommand, RequestResponse<MileageMovementRow>>
{
  public async Task<RequestResponse<MileageMovementRow>> Handle(
    RecordMovementCommand command,
    CancellationToken ct
  )
  {
    try
    {
      return await RecordAsync(command, ct);
    }
    catch (Exception exception) when (db.IsWriteConflict(exception))
    {
      return MileageMutation.Conflict();
    }
  }

  private async Task<RequestResponse<MileageMovementRow>> RecordAsync(
    RecordMovementCommand command,
    CancellationToken ct
  )
  {
    var actor = await MileageAccess.ActorAsync(db, caller, roles, false, ct);
    if (actor is null)
      return Fail("Access denied.", 403);
    var request = command.Request;
    var now = clock.GetUtcNow();
    if (
      request is null
      || request.IdempotencyKey == Guid.Empty
      || request.TruckId == Guid.Empty
      || !MileageAllocation.ValidPurpose(request.Purpose)
      || !MileageAllocation.ValidCargoState(request.CargoState)
      || request.CargoState == "bobtail" && request.TrailerId.HasValue
      || request.CargoState is "empty" or "loaded"
        && !request.TrailerId.HasValue
      || request.DriverId.HasValue && request.DriverId == request.CoDriverId
      || string.IsNullOrWhiteSpace(request.FromLocation)
      || string.IsNullOrWhiteSpace(request.ToLocation)
      || request.FromLocation.Length > 500
      || request.ToLocation.Length > 500
      || request.EndedAt.HasValue && !request.StartedAt.HasValue
      || request.StartedAt >= request.EndedAt
      || request.StartedAt > now
      || request.EndedAt > now
      || request.StartedAt?.Year < 2000
      || request.EndedAt?.Year < 2000
    )
      return Fail(
        "Provide a valid movement, assignments and actual time range."
      );
    var hash = Convert.ToHexString(
      SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request))
    );
    await using var transaction = await db.Database.BeginTransactionAsync(
      IsolationLevel.Serializable,
      ct
    );
    var existing = await db
      .Movements.AsNoTracking()
      .SingleOrDefaultAsync(
        x => x.IdempotencyKey == request.IdempotencyKey,
        ct
      );
    if (existing is not null)
      return existing.RequestHash == hash
        ? await ReplyAsync(existing, ct)
        : Fail("The retry key belongs to another movement.", 409);
    var drivers = new[] { request.DriverId, request.CoDriverId }
      .OfType<Guid>()
      .Distinct()
      .ToArray();
    var loads = new[]
    {
      request.PreviousDispatchId,
      request.NextDispatchId,
      request.CarriedDispatchId,
    }
      .OfType<Guid>()
      .Distinct()
      .ToArray();
    if (
      !await db.Trucks.AnyAsync(x => x.Id == request.TruckId, ct)
      || await db.Drivers.CountAsync(x => drivers.Contains(x.Id), ct)
        != drivers.Length
      || request.TrailerId is { } trailer
        && !await db.Trailers.AnyAsync(x => x.Id == trailer, ct)
      || await db.Dispatches.CountAsync(x => loads.Contains(x.Id), ct)
        != loads.Length
    )
      return Fail("A selected fleet resource or load does not exist.");
    if (request.ExecutionLegId is { } legId)
    {
      var leg = await db
        .ExecutionLegs.AsNoTracking()
        .SingleOrDefaultAsync(x => x.Id == legId, ct);
      if (
        leg is null
        || leg.TruckId != request.TruckId
        || leg.DriverId != request.DriverId
        || leg.CoDriverId != request.CoDriverId
        || leg.TrailerId != request.TrailerId
        || request.StartedAt.HasValue
          && (
            leg.StartedAt is null
            || request.StartedAt.Value.UtcDateTime < leg.StartedAt
          )
        || leg.CompletedAt.HasValue
          && (
            request.EndedAt is null
            || request.EndedAt.Value.UtcDateTime > leg.CompletedAt
          )
      )
        return Fail(
          "Movement assignments or times do not match the execution."
        );
      if (!await db.LockExecutionLegAsync(legId, leg.Revision, ct))
        return Fail("Execution changed. Reload before recording mileage.", 409);
    }
    if (
      request.StartedAt is { } start
      && await db.Movements.AnyAsync(
        MovementIntervals.OverlappingTruck(
          request.TruckId,
          start.UtcDateTime,
          request.EndedAt?.UtcDateTime
        ),
        ct
      )
    )
      return Fail("The truck already has a movement in that time range.", 409);
    var policy =
      await db
        .MileageAllocationPolicies.AsNoTracking()
        .SingleOrDefaultAsync(
          x => x.Id == MileageAllocationPolicy.SingletonId,
          ct
        ) ?? new();
    var movement = new Movement
    {
      Id = Guid.NewGuid(),
      IdempotencyKey = request.IdempotencyKey,
      RequestHash = hash,
      TruckId = request.TruckId,
      DriverId = request.DriverId,
      CoDriverId = request.CoDriverId,
      TrailerId = request.TrailerId,
      ExecutionLegId = request.ExecutionLegId,
      Purpose = request.Purpose,
      CargoState = request.CargoState,
      PreviousDispatchId = request.PreviousDispatchId,
      NextDispatchId = request.NextDispatchId,
      CarriedDispatchId = request.CarriedDispatchId,
      FromLocation = request.FromLocation.Trim(),
      ToLocation = request.ToLocation.Trim(),
      StartedAt = request.StartedAt?.UtcDateTime,
      EndedAt = request.EndedAt?.UtcDateTime,
      RecordedAt = now.UtcDateTime,
      RecordedBy = actor.Value,
      Revision = 1,
    };
    db.Movements.Add(movement);
    var allocation = MileageMutation.Allocate(
      movement,
      MileageAllocation.Resolve(movement, policy),
      policy.Revision,
      false,
      actor.Value,
      now.UtcDateTime
    );
    db.MovementAllocationEvents.Add(allocation);
    try
    {
      await db.SaveChangesAsync(ct);
      await transaction.CommitAsync(ct);
    }
    catch (Exception exception) when (db.IsWriteConflict(exception))
    {
      await transaction.DisposeAsync();
      db.Entry(movement).State = EntityState.Detached;
      db.Entry(allocation).State = EntityState.Detached;
      var retry = await db
        .Movements.AsNoTracking()
        .SingleOrDefaultAsync(
          x => x.IdempotencyKey == request.IdempotencyKey,
          ct
        );
      if (retry is not null)
        return retry.RequestHash == hash
          ? await ReplyAsync(retry, ct)
          : Fail("The retry key belongs to another movement.", 409);
      return MileageMutation.Conflict();
    }
    return await ReplyAsync(movement, ct);
  }

  private async Task<RequestResponse<MileageMovementRow>> ReplyAsync(
    Movement movement,
    CancellationToken ct
  ) =>
    RequestResponse<MileageMovementRow>.Ok(
      (
        await MileageMovementView.WithNumbersAsync(
          db,
          [MileageMovementView.From(movement)],
          ct
        )
      )[0]
    );

  private static RequestResponse<MileageMovementRow> Fail(
    string message,
    int status = 400
  ) => RequestResponse<MileageMovementRow>.Fail(message, status);
}
