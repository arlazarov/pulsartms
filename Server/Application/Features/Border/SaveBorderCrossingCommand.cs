using System.Data;
using System.Text.Json;
using Application.Features.Border.Interfaces;
using Application.Features.Border.Models;
using Application.Features.Border.Services;
using Application.Features.Execution.Models;
using Application.Features.Shipments.Models;
using Application.Features.Shipments.Services;
using Application.Models;
using Entity = Domain.Entities.Border.BorderCrossing;

namespace Application.Features.Border;

public sealed record SaveBorderCrossingCommand(SaveBorderCrossing Request)
  : IRequest<RequestResponse<BorderCrossing>>;

public sealed class SaveBorderCrossingHandler(
  IAppDbContext db,
  ICurrentUser caller,
  IUserRoleService roles,
  TimeProvider clock,
  IBorderDataProtection protection
) : IRequestHandler<SaveBorderCrossingCommand, RequestResponse<BorderCrossing>>
{
  public async Task<RequestResponse<BorderCrossing>> Handle(
    SaveBorderCrossingCommand command,
    CancellationToken ct
  )
  {
    var actor = await BorderAccess.Actor(db, caller, roles, ct);
    if (actor is null)
      return Fail("Access denied.", 403);
    var request = command.Request;
    if (
      request is null
      || request.RequestId == Guid.Empty
      || request.ExpectedRevision is < 0 or long.MaxValue
    )
      return Fail("Provide the opened revision and save identity.", 400);
    var error = BorderRules.Validate(request.Crossing);
    if (error is not null)
      return Fail(error, 400);
    var hash = ExecutionCommandSupport.Hash(command);
    var draft = JsonSerializer.Deserialize<BorderCrossing>(
      JsonSerializer.Serialize(request.Crossing)
    )!;
    await using var transaction = await db.Database.BeginTransactionAsync(
      IsolationLevel.Serializable,
      ct
    );
    var receipt = await db
      .BorderSaveReceipts.AsNoTracking()
      .SingleOrDefaultAsync(x => x.Id == request.RequestId, ct);
    if (receipt is not null)
      return
        receipt.ActorId == actor
        && receipt.RequestHash == hash
        && receipt.CrossingId == draft.Id
        ? RequestResponse<BorderCrossing>.Ok(
          JsonSerializer.Deserialize<BorderCrossing>(
            protection.Unprotect(draft.Id, receipt.ProtectedResponse)
          )!
        )
        : Fail("This retry identity belongs to another save.");
    var row = await db
      .BorderCrossings.AsSplitQuery()
      .SingleOrDefaultAsync(x => x.Id == draft.Id, ct);
    if ((row?.Revision ?? 0) != request.ExpectedRevision)
      return Fail(
        "Crossing changed. Your draft is retained; reload before saving."
      );
    if (draft.SourceLegId is { } legId)
    {
      if (
        !await db.ExecutionLegs.AnyAsync(
          x => x.Id == legId && x.Revision == draft.SourceRevision,
          ct
        )
        || !await db.ExecutionLegStops.AnyAsync(
          x => x.ExecutionLegId == legId && x.Id == draft.SourceStopId,
          ct
        )
      )
        return Fail(
          "The execution assignment changed. Review the selected crossing assignment."
        );
    }
    foreach (var link in draft.Shipments)
    {
      var old = row?.Shipments.SingleOrDefault(x => x.Id == link.Id);
      if (
        old is not null
        && old.ShipmentId == link.ShipmentId
        && old.ShipmentRevision == link.ShipmentRevision
      )
        link.Snapshot = JsonSerializer.Deserialize<Shipment>(
          old.ShipmentSnapshotJson
        )!;
      else
      {
        var shipment = await db
          .Shipments.AsNoTracking()
          .SingleOrDefaultAsync(x => x.Id == link.ShipmentId, ct);
        if (shipment is null || shipment.Revision != link.ShipmentRevision)
          return Fail(
            "A selected shipment changed. Select its current version again."
          );
        link.Snapshot = ShipmentMapping.Project(shipment);
      }
    }
    foreach (var member in draft.Crew)
    {
      if (member.DriverId is not { } driverId)
        continue;
      var name = await db
        .Drivers.AsNoTracking()
        .Where(x => x.Id == driverId)
        .Select(x => x.Name)
        .SingleOrDefaultAsync(ct);
      if (name is null)
        return Fail("Select an existing fleet driver.", 400);
      var old = row?.Crew.SingleOrDefault(x =>
        x.Id == member.Id && x.DriverId == driverId
      );
      member.DisplayName = old?.DisplayName ?? name;
    }
    foreach (var vehicle in draft.Equipment)
    {
      if (vehicle.TruckId is { } truckId)
      {
        var truck = await db
          .Trucks.AsNoTracking()
          .SingleOrDefaultAsync(x => x.Id == truckId, ct);
        if (truck is null)
          return Fail("Select an existing truck.", 400);
        var old = row?.Equipment.SingleOrDefault(x =>
          x.Id == vehicle.Id && x.TruckId == truckId
        );
        vehicle.UnitNumber = old?.UnitNumber ?? truck.UnitNumber;
        vehicle.Vin = old?.Vin ?? truck.Vin;
      }
      if (vehicle.TrailerId is { } trailerId)
      {
        var trailer = await db
          .Trailers.AsNoTracking()
          .SingleOrDefaultAsync(x => x.Id == trailerId, ct);
        if (trailer is null)
          return Fail("Select an existing trailer.", 400);
        var old = row?.Equipment.SingleOrDefault(x =>
          x.Id == vehicle.Id && x.TrailerId == trailerId
        );
        vehicle.UnitNumber = old?.UnitNumber ?? trailer.UnitNumber;
        vehicle.Vin = old?.Vin ?? trailer.Vin;
      }
    }
    if (row is null)
    {
      row = new Entity { Id = draft.Id };
      db.BorderCrossings.Add(row);
    }
    BorderMapping.Apply(row, draft, protection);
    row.Revision++;
    row.UpdatedAt = clock.GetUtcNow().UtcDateTime;
    row.UpdatedBy = actor.Value;
    var result = BorderMapping.Project(row, protection);
    db.BorderSaveReceipts.Add(
      new()
      {
        Id = request.RequestId,
        ActorId = actor.Value,
        CrossingId = row.Id,
        RequestHash = hash,
        ProtectedResponse = protection.Protect(
          row.Id,
          JsonSerializer.Serialize(result)
        ),
      }
    );
    try
    {
      await db.SaveChangesAsync(ct);
      await transaction.CommitAsync(ct);
    }
    catch (Exception exception) when (db.IsWriteConflict(exception))
    {
      return Fail("Crossing changed. Reload before saving your draft.");
    }
    return RequestResponse<BorderCrossing>.Ok(result);
  }

  private static RequestResponse<BorderCrossing> Fail(
    string error,
    int status = 409
  ) => RequestResponse<BorderCrossing>.Fail(error, status);
}
