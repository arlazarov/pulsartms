using System.Data;
using Application.Caching;
using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Services;
using Application.Features.Execution.Models;
using Application.Features.Routing.Background;
using Application.Models;
using Domain.Entities.Dispatch;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Application.Features.Dispatch.Commands;

public sealed record CreateDispatchCommand(CreateDispatchRequest Request)
  : IRequest<RequestResponse<DispatchWorkspaceResponse>>;

public sealed class CreateDispatchHandler(
  IAppDbContext db,
  ICurrentUser caller,
  IUserRoleService roles,
  TimeProvider clock,
  ReadCache reads,
  RoutePreparationQueue preparation
)
  : IRequestHandler<
    CreateDispatchCommand,
    RequestResponse<DispatchWorkspaceResponse>
  >
{
  public async Task<RequestResponse<DispatchWorkspaceResponse>> Handle(
    CreateDispatchCommand command,
    CancellationToken ct
  )
  {
    var actor = await ExecutionCommandSupport.ActorAsync(db, caller, roles, ct);
    if (actor is null)
      return Fail("Access denied.", 403);
    if (command.Request is null || command.Request.IdempotencyKey == Guid.Empty)
      return Fail("Provide a creation request identity.", 400);
    var request = DispatchWorkspaceData.Read<CreateDispatchRequest>(
      DispatchWorkspaceData.Write(command.Request)
    );
    var hash = DispatchWorkspaceData.Hash(new CreateDispatchCommand(request));
    var metadata = new DispatchWorkspaceMetadata
    {
      OrderNumber = request.OrderNumber,
      CustomerName = request.CustomerName,
      Price = request.Price,
      Currency = request.Currency,
    };
    var problem = DispatchWorkspaceRules.ValidateNew(metadata, request.Stops);
    if (problem is not null)
      return Fail(problem, 400);
    await using var tx = await db.Database.BeginTransactionAsync(
      IsolationLevel.Serializable,
      ct
    );
    try
    {
      var receipt = await db
        .DispatchWorkspaceRevisions.AsNoTracking()
        .SingleOrDefaultAsync(
          x => x.IdempotencyKey == request.IdempotencyKey,
          ct
        );
      if (receipt is not null)
        return receipt.RequestHash == hash && receipt.RecordedBy == actor
          ? RequestResponse<DispatchWorkspaceResponse>.Ok(
            DispatchWorkspaceData.Read<DispatchWorkspaceResponse>(
              receipt.SnapshotJson
            )
          )
          : Fail("The retry identity belongs to another operation.", 409);
      var ids = request.Stops.Select(x => x.Id).ToArray();
      if (
        await db.DispatchStops.AnyAsync(x => ids.Contains(x.Id), ct)
        || await db.ExecutionLegStops.AnyAsync(x => ids.Contains(x.Id), ct)
      )
        return Fail("A stop identity is already in use.", 409);
      var now = clock.GetUtcNow().UtcDateTime;
      var number = (await DispatchNumbers.ReserveAsync(db, [null], ct))[0];
      var load = new Load
      {
        Id = Guid.NewGuid(),
        LoadNumber = number,
        Status = "unassigned",
        OrderDate = DateOnly.FromDateTime(now),
      };
      DispatchWorkspaceData.ApplyCommercial(load, metadata);
      foreach (var (value, index) in request.Stops.Select((x, i) => (x, i)))
      {
        var stop = new DispatchStop
        {
          Id = value.Id,
          DispatchId = load.Id,
          Sequence = index + 1,
          Job = value.Job,
        };
        DispatchWorkspaceData.Apply(stop, value);
        stop.SourceAddressJson = StopAddress.From(stop).Serialize();
        stop.AddressVerifiedAt = now;
        load.Stops.Add(stop);
      }
      load.ShipDate = load.Stops[0].ScheduledDate;
      load.DeliveryDate = load.Stops[^1].ScheduledDate;
      db.Dispatches.Add(load);
      db.DispatchWorkspaces.Add(
        new()
        {
          Id = load.Id,
          OwnsStops = true,
          OwnsCommercial = true,
          MetadataJson = DispatchWorkspaceData.Write(metadata),
          StopExtrasJson = DispatchWorkspaceData.Write(
            request.Stops.ToDictionary(x => x.Id)
          ),
          Revision = 1,
          RecordedAt = now,
          RecordedBy = actor.Value,
        }
      );
      var history = new DispatchWorkspaceRevision
      {
        Id = Guid.NewGuid(),
        DispatchId = load.Id,
        Revision = 1,
        IdempotencyKey = request.IdempotencyKey,
        RequestHash = hash,
        RecordedAt = now,
        RecordedBy = actor.Value,
        ActorName = await db
          .Users.Where(x => x.Id == actor.Value)
          .Select(x => x.Name)
          .SingleAsync(ct),
        Summary = "Created load.",
      };
      db.DispatchWorkspaceRevisions.Add(history);
      await db.SaveChangesAsync(ct);
      var saved = await DispatchWorkspaceReader.ReadAsync(
        db,
        load.Id,
        true,
        ct
      );
      history.SnapshotJson = DispatchWorkspaceData.Write(saved!.Response);
      await db.SaveChangesAsync(ct);
      await tx.CommitAsync(ct);
      reads.Invalidate("dispatch");
      reads.Invalidate("board");
      preparation.MarkDirty(load.Id);
      return RequestResponse<DispatchWorkspaceResponse>.Ok(saved.Response);
    }
    catch (Exception ex) when (db.IsWriteConflict(ex))
    {
      return Fail(
        "Load creation changed concurrently. Retry the same request.",
        409
      );
    }
  }

  private static RequestResponse<DispatchWorkspaceResponse> Fail(
    string message,
    int status
  ) => RequestResponse<DispatchWorkspaceResponse>.Fail(message, status);
}
