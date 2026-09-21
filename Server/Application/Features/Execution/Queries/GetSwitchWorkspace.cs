using Application.Models;
using Domain.Entities.Dispatch;
using Domain.Entities.Execution;
using Domain.Models.Execution;
using DispatchEntity = Domain.Entities.Dispatch.Dispatch;

namespace Application.Features.Execution.Queries;

public sealed record GetSwitchWorkspaceQuery(
  Guid DispatchId,
  Guid? SecondDispatchId = null
) : IRequest<RequestResponse<SwitchWorkspace>>;

public sealed record GetSwitchDetailsQuery(Guid SwitchId)
  : IRequest<RequestResponse<SwitchDetails>>;

public sealed class GetSwitchWorkspaceHandler(
  IAppDbContext db,
  ICurrentUser caller,
  IUserRoleService roles
) : IRequestHandler<GetSwitchWorkspaceQuery, RequestResponse<SwitchWorkspace>>
{
  public async Task<RequestResponse<SwitchWorkspace>> Handle(
    GetSwitchWorkspaceQuery request,
    CancellationToken ct
  )
  {
    if (await ExecutionCommandSupport.ActorAsync(db, caller, roles, ct) is null)
      return RequestResponse<SwitchWorkspace>.Fail("Access denied.", 403);
    var ids = new[] { (Guid?)request.DispatchId, request.SecondDispatchId }
      .Where(x => x.HasValue)
      .Select(x => x!.Value)
      .Distinct()
      .ToArray();
    var loads = await db
      .Dispatches.AsNoTracking()
      .Include(x => x.Stops)
      .Where(x => ids.Contains(x.Id))
      .ToListAsync(ct);
    if (loads.Count != ids.Length)
      return RequestResponse<SwitchWorkspace>.Fail(
        "A selected load was not found.",
        404
      );
    var links = await db
      .LoadExecutionLegs.AsNoTracking()
      .Where(x => ids.Contains(x.DispatchId))
      .Include(x => x.ExecutionLeg)
      .ToListAsync(ct);
    var options = new List<SwitchLoadOption>();
    foreach (var load in loads.OrderBy(x => Array.IndexOf(ids, x.Id)))
    {
      var linked = links.Where(x => x.DispatchId == load.Id).ToArray();
      var active = linked
        .Where(x => x.ExecutionLeg.Status == "active")
        .ToArray();
      var available =
        active.Length > 0
          ? active
          : linked.Where(x => x.ExecutionLeg.Status == "planned").ToArray();
      var leg = available.Length == 1 ? available[0].ExecutionLeg : null;
      var terminal = load.Status is "completed" or "cancelled" or "canceled";
      var reason =
        terminal
          ? "This load is closed in the source. "
            + "Existing switches remain available."
        : linked.Length > 0 && leg is null
          ? "Review the native assignments before planning another switch."
        : leg?.SourceReviewReason;
      var outgoing =
        terminal || linked.Length > 0 && leg is null ? null
        : leg is null ? Assignment(load)
        : ExecutionCommandSupport.Assignment(leg);
      options.Add(
        new(
          load.Id,
          load.LoadNumber,
          ExecutionSnapshots.Fingerprint(load),
          outgoing,
          leg?.Id,
          leg?.Revision,
          load.Stops.OrderBy(x => x.Sequence)
            .Select(SwitchReads.Visit)
            .ToArray()
        )
        {
          SourceReviewReason = reason,
        }
      );
    }
    var operations = await db
      .DispatchSwitchOperations.AsNoTracking()
      .Where(x => x.Participants.Any(p => ids.Contains(p.DispatchId)))
      .OrderByDescending(x => x.RecordedAt)
      .Take(20)
      .Select(x => x.Id)
      .ToArrayAsync(ct);
    return RequestResponse<SwitchWorkspace>.Ok(
      new(
        options,
        await db
          .Trucks.AsNoTracking()
          .Where(x => x.IsActive)
          .OrderBy(x => x.UnitNumber)
          .Select(x => new SwitchResourceOption(x.Id, x.UnitNumber))
          .ToListAsync(ct),
        await db
          .Drivers.AsNoTracking()
          .Where(x => x.IsActive)
          .OrderBy(x => x.Name)
          .Select(x => new SwitchResourceOption(x.Id, x.Name))
          .ToListAsync(ct),
        await db
          .Trailers.AsNoTracking()
          .Where(x => x.IsActive)
          .OrderBy(x => x.UnitNumber)
          .Select(x => new SwitchResourceOption(x.Id, x.UnitNumber))
          .ToListAsync(ct),
        await SwitchReads.DetailsAsync(db, operations, ct)
      )
    );
  }

  private static ExecutionAssignment? Assignment(DispatchEntity load)
  {
    var assignments = load
      .Stops.Where(x => x.TruckId.HasValue)
      .Select(x => new ExecutionAssignment(
        x.TruckId!.Value,
        x.DriverId,
        x.TrailerId,
        x.CoDriverId
      ))
      .Distinct()
      .ToArray();
    return assignments.Length == 1 ? assignments[0]
      : assignments.Length == 0 && load.TruckId is { } truck
        ? new(truck, load.DriverId, load.TrailerId)
      : null;
  }
}

public sealed class GetSwitchDetailsHandler(
  IAppDbContext db,
  ICurrentUser caller,
  IUserRoleService roles
) : IRequestHandler<GetSwitchDetailsQuery, RequestResponse<SwitchDetails>>
{
  public async Task<RequestResponse<SwitchDetails>> Handle(
    GetSwitchDetailsQuery request,
    CancellationToken ct
  )
  {
    if (await ExecutionCommandSupport.ActorAsync(db, caller, roles, ct) is null)
      return RequestResponse<SwitchDetails>.Fail("Access denied.", 403);
    var item = (
      await SwitchReads.DetailsAsync(db, [request.SwitchId], ct)
    ).SingleOrDefault();
    return item is null
      ? RequestResponse<SwitchDetails>.Fail("Switch not found.", 404)
      : RequestResponse<SwitchDetails>.Ok(item);
  }
}

internal static class SwitchReads
{
  public static SwitchVisitOption Visit(DispatchStop stop) =>
    new(
      stop.Id,
      stop.Sequence,
      stop.Job,
      stop.Name,
      string.Join(
        ", ",
        new[]
        {
          stop.Address,
          stop.City,
          stop.Province,
          stop.ZipCode,
          stop.Country,
        }.Where(x => !string.IsNullOrWhiteSpace(x))
      ),
      stop.Latitude,
      stop.Longitude,
      stop.IsCompleted
    )
    {
      ScheduledDate = stop.ScheduledDate,
      ScheduledTime = stop.ScheduledTime,
      ScheduledDate2 = stop.ScheduledDate2,
      ScheduledTime2 = stop.ScheduledTime2,
      IsWindow = stop.IsWindow,
    };

  public static async Task<IReadOnlyList<SwitchDetails>> DetailsAsync(
    IAppDbContext db,
    IReadOnlyCollection<Guid> ids,
    CancellationToken ct
  )
  {
    var operations = await db
      .DispatchSwitchOperations.AsNoTracking()
      .Where(x => ids.Contains(x.Id))
      .Include(x => x.Participants)
      .OrderByDescending(x => x.RecordedAt)
      .ToListAsync(ct);
    var participants = operations.SelectMany(x => x.Participants).ToArray();
    var legIds = participants
      .SelectMany(x => new[] { x.OutgoingLegId, x.IncomingLegId })
      .Distinct()
      .ToArray();
    var legs = await db
      .ExecutionLegs.AsNoTracking()
      .Where(x => legIds.Contains(x.Id))
      .ToDictionaryAsync(x => x.Id, ct);
    var loadIds = participants.Select(x => x.DispatchId).Distinct().ToArray();
    var loads = await db
      .Dispatches.AsNoTracking()
      .Where(x => loadIds.Contains(x.Id))
      .ToDictionaryAsync(x => x.Id, x => x.LoadNumber, ct);
    var truckIds = legs.Values.Select(x => x.TruckId).Distinct().ToArray();
    var driverIds = legs
      .Values.Select(ExecutionCommandSupport.Assignment)
      .SelectMany(x => new[] { x.DriverId, x.CoDriverId })
      .Where(x => x.HasValue)
      .Select(x => x!.Value)
      .Distinct()
      .ToArray();
    var trailerIds = legs
      .Values.Where(x => x.TrailerId.HasValue)
      .Select(x => x.TrailerId!.Value)
      .Distinct()
      .ToArray();
    var trucks = await db
      .Trucks.AsNoTracking()
      .Where(x => truckIds.Contains(x.Id))
      .ToDictionaryAsync(x => x.Id, x => x.UnitNumber, ct);
    var drivers = await db
      .Drivers.AsNoTracking()
      .Where(x => driverIds.Contains(x.Id))
      .ToDictionaryAsync(x => x.Id, x => x.Name, ct);
    var trailers = await db
      .Trailers.AsNoTracking()
      .Where(x => trailerIds.Contains(x.Id))
      .ToDictionaryAsync(x => x.Id, x => x.UnitNumber, ct);
    var result = new List<SwitchDetails>();
    foreach (var operation in operations)
    {
      var rows = new List<SwitchParticipantDetails>();
      foreach (
        var participant in operation.Participants.OrderBy(x => x.DispatchId)
      )
      {
        var before = legs[participant.OutgoingLegId];
        var after = legs[participant.IncomingLegId];
        var live =
          !participant.IsCancelled
          && operation.Status is "planned" or "in_progress";
        var canReceive =
          live
          && participant.ReleasedBy.HasValue
          && !participant.ReceivedBy.HasValue
          && after.Status == "planned"
          && await ExecutionResources.ActiveAsync(
            db,
            [ExecutionCommandSupport.Assignment(after)],
            ct
          )
          && !await ExecutionResources.ConflictsAsync(
            db,
            ExecutionCommandSupport.Assignment(after),
            participant.Id,
            ct
          );
        rows.Add(
          new(
            participant.Id,
            participant.DispatchId,
            loads.GetValueOrDefault(participant.DispatchId),
            participant.TransferKind,
            participant.Revision,
            before.Id,
            before.Revision,
            after.Id,
            after.Revision,
            Assignment(before),
            Assignment(after),
            participant.ReleaseVisitId,
            participant.ReceiveVisitId,
            participant.PlannedReleaseAt,
            participant.PlannedReceiveAt,
            participant.ReleasedAt,
            participant.ReceivedAt,
            live
              && before.Status == "active"
              && !participant.ReleasedBy.HasValue,
            canReceive
          )
          {
            Released = participant.ReleasedBy.HasValue,
            Received = participant.ReceivedBy.HasValue,
            SourceReviewReason =
              before.SourceReviewReason ?? after.SourceReviewReason,
            SourceReviewExecutionLegId =
              before.SourceReviewReason is not null
              && before.Status is "active" or "planned"
                ? before.Id
              : after.SourceReviewReason is not null
              && after.Status is "active" or "planned"
                ? after.Id
              : null,
          }
        );
      }
      result.Add(
        new(
          operation.Id,
          operation.Status,
          operation.Revision,
          operation.SiteName,
          operation.Latitude,
          operation.Longitude,
          operation.Status == "planned"
            && operation.Participants.All(x =>
              !x.ReleasedBy.HasValue && !x.ReceivedBy.HasValue
            ),
          rows
        )
      );
    }
    return result;

    SwitchAssignmentOption Assignment(ExecutionLeg leg) =>
      new(
        ExecutionCommandSupport.Assignment(leg),
        trucks.GetValueOrDefault(leg.TruckId, ""),
        ExecutionCommandSupport.Assignment(leg).DriverId is { } driver
          ? drivers.GetValueOrDefault(driver, "")
          : "",
        leg.TrailerId is { } trailer
          ? trailers.GetValueOrDefault(trailer, "")
          : ""
      );
  }
}
