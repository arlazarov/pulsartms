using System.Data;
using System.Text.Json;
using Application.Features.Execution.Models;
using Application.Features.Shipments.Models;
using Application.Features.Shipments.Services;
using Application.Models;
using Domain.Entities.Shipments;
using Shipment = Application.Features.Shipments.Models.Shipment;
using ShipmentEntity = Domain.Entities.Shipments.Shipment;

namespace Application.Features.Shipments;

public sealed record GetShipmentStops(Guid LoadId)
  : IRequest<RequestResponse<List<ShipmentStopOption>>>;

public sealed record GetShipments(Guid LoadId)
  : IRequest<RequestResponse<List<Shipment>>>;

public sealed record SaveShipmentCommand(SaveShipment Request)
  : IRequest<RequestResponse<Shipment>>;

public sealed record CheckShipment(Shipment Shipment)
  : IRequest<RequestResponse<List<ShipmentIssue>>>;

public sealed class ShipmentsHandler(
  IAppDbContext db,
  ICurrentUser caller,
  IUserRoleService roles,
  TimeProvider clock
)
  : IRequestHandler<
    GetShipmentStops,
    RequestResponse<List<ShipmentStopOption>>
  >,
    IRequestHandler<GetShipments, RequestResponse<List<Shipment>>>,
    IRequestHandler<SaveShipmentCommand, RequestResponse<Shipment>>,
    IRequestHandler<CheckShipment, RequestResponse<List<ShipmentIssue>>>
{
  public async Task<RequestResponse<List<ShipmentStopOption>>> Handle(
    GetShipmentStops query,
    CancellationToken ct
  )
  {
    if (await ExecutionCommandSupport.ActorAsync(db, caller, roles, ct) is null)
      return RequestResponse<List<ShipmentStopOption>>.Fail(
        "Access denied.",
        403
      );
    var rows = await db
      .DispatchStops.AsNoTracking()
      .Where(x => x.DispatchId == query.LoadId)
      .OrderBy(x => x.Sequence)
      .Select(x => new ShipmentStopOption(
        x.Id,
        x.Job,
        x.Name,
        x.Address,
        x.City,
        x.Province,
        x.Country,
        x.ZipCode
      ))
      .ToListAsync(ct);
    return RequestResponse<List<ShipmentStopOption>>.Ok(rows);
  }

  public async Task<RequestResponse<List<Shipment>>> Handle(
    GetShipments query,
    CancellationToken ct
  )
  {
    if (await ExecutionCommandSupport.ActorAsync(db, caller, roles, ct) is null)
      return RequestResponse<List<Shipment>>.Fail("Access denied.", 403);
    if (!await db.Dispatches.AnyAsync(x => x.Id == query.LoadId, ct))
      return RequestResponse<List<Shipment>>.Fail("Load not found.", 404);
    var rows = await db
      .Shipments.AsNoTracking()
      .Where(x => x.LoadId == query.LoadId)
      .OrderBy(x => x.UpdatedAt)
      .Take(100)
      .ToListAsync(ct);
    return RequestResponse<List<Shipment>>.Ok(
      rows.Select(ShipmentMapping.Project).ToList()
    );
  }

  public async Task<RequestResponse<List<ShipmentIssue>>> Handle(
    CheckShipment query,
    CancellationToken ct
  )
  {
    if (await ExecutionCommandSupport.ActorAsync(db, caller, roles, ct) is null)
      return RequestResponse<List<ShipmentIssue>>.Fail("Access denied.", 403);
    var error = ShipmentRules.Validate(query.Shipment);
    return error is null
      ? RequestResponse<List<ShipmentIssue>>.Ok(
        ShipmentRules.Missing(query.Shipment)
      )
      : RequestResponse<List<ShipmentIssue>>.Fail(error);
  }

  public async Task<RequestResponse<Shipment>> Handle(
    SaveShipmentCommand command,
    CancellationToken ct
  )
  {
    var actor = await ExecutionCommandSupport.ActorAsync(db, caller, roles, ct);
    if (actor is null)
      return RequestResponse<Shipment>.Fail("Access denied.", 403);
    var request = command.Request;
    if (
      request is null
      || request.RequestId == Guid.Empty
      || request.ExpectedRevision is < 0 or long.MaxValue
    )
      return RequestResponse<Shipment>.Fail(
        "Provide the opened save identity."
      );
    var error = ShipmentRules.Validate(request.Shipment);
    if (error is not null)
      return RequestResponse<Shipment>.Fail(error);
    var hash = ExecutionCommandSupport.Hash(command);
    await using var transaction = await db.Database.BeginTransactionAsync(
      IsolationLevel.Serializable,
      ct
    );
    var receipt = await db
      .ShipmentSaveReceipts.AsNoTracking()
      .SingleOrDefaultAsync(x => x.Id == request.RequestId, ct);
    if (receipt is not null)
      return receipt.ActorId == actor && receipt.RequestHash == hash
        ? RequestResponse<Shipment>.Ok(
          JsonSerializer.Deserialize<Shipment>(receipt.ResponseJson)!
        )
        : RequestResponse<Shipment>.Fail(
          "This retry belongs to another save.",
          409
        );
    var draft = request.Shipment;
    if (!await db.Dispatches.AnyAsync(x => x.Id == draft.LoadId, ct))
      return RequestResponse<Shipment>.Fail("Load not found.", 404);
    foreach (
      var (stopId, pickup) in new[]
      {
        (draft.PickupStopId, true),
        (draft.DeliveryStopId, false),
      }
    )
    {
      if (stopId is null)
        continue;
      var stop = await db
        .DispatchStops.AsNoTracking()
        .SingleOrDefaultAsync(
          x => x.Id == stopId && x.DispatchId == draft.LoadId,
          ct
        );
      if (
        stop is null
        || (
          pickup
            ? stop.Job is not ("Pick Up" or "Pickup")
            : stop.Job is not ("Drop Off" or "Delivery")
        )
      )
        return RequestResponse<Shipment>.Fail(
          "Select a matching stop on this load."
        );
    }
    var row = await db.Shipments.SingleOrDefaultAsync(
      x => x.Id == draft.Id,
      ct
    );
    if (
      (row?.Revision ?? 0) != request.ExpectedRevision
      || row is not null && row.LoadId != draft.LoadId
    )
      return RequestResponse<Shipment>.Fail(
        "Shipment changed. Reload before saving.",
        409
      );
    if (row is null)
    {
      if (
        await db.Shipments.CountAsync(x => x.LoadId == draft.LoadId, ct) >= 100
      )
        return RequestResponse<Shipment>.Fail(
          "A load supports up to 100 shipments."
        );
      row = new() { Id = draft.Id, LoadId = draft.LoadId };
      db.Shipments.Add(row);
    }
    ShipmentMapping.Apply(row, draft);
    row.Revision++;
    row.UpdatedAt = clock.GetUtcNow().UtcDateTime;
    row.UpdatedBy = actor.Value;
    var result = ShipmentMapping.Project(row);
    db.ShipmentSaveReceipts.Add(
      new()
      {
        Id = request.RequestId,
        AggregateId = row.Id,
        ActorId = actor.Value,
        RequestHash = hash,
        ResponseJson = JsonSerializer.Serialize(result),
        RecordedAt = row.UpdatedAt,
      }
    );
    try
    {
      await db.SaveChangesAsync(ct);
      await transaction.CommitAsync(ct);
    }
    catch (Exception exception) when (db.IsWriteConflict(exception))
    {
      return RequestResponse<Shipment>.Fail(
        "Shipment changed. Reload before saving.",
        409
      );
    }
    return RequestResponse<Shipment>.Ok(result);
  }
}
