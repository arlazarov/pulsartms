using System.Data;
using Application.Features.Border.Interfaces;
using Application.Features.Border.Models;
using Application.Features.Border.Services;
using Application.Features.Shipments.Models;
using Application.Models;

namespace Application.Features.Border;

public sealed record GetBorderPorts
  : IRequest<RequestResponse<List<BorderPort>>>;

public sealed record ListBorderCrossings(int Offset = 0)
  : IRequest<RequestResponse<List<BorderSummary>>>;

public sealed record GetBorderCrossing(Guid Id)
  : IRequest<RequestResponse<BorderCrossing>>;

public sealed record FindBorderShipments(string Search)
  : IRequest<RequestResponse<List<BorderShipmentOption>>>;

public sealed record GetBorderAssignments(Guid LoadId)
  : IRequest<RequestResponse<List<BorderAssignmentOption>>>;

public sealed record CheckBorderCrossing(BorderCrossing Crossing)
  : IRequest<RequestResponse<List<ShipmentIssue>>>;

public sealed class BorderQueries(
  IAppDbContext db,
  ICurrentUser caller,
  IUserRoleService roles,
  IBorderDataProtection protection
)
  : IRequestHandler<GetBorderPorts, RequestResponse<List<BorderPort>>>,
    IRequestHandler<ListBorderCrossings, RequestResponse<List<BorderSummary>>>,
    IRequestHandler<GetBorderCrossing, RequestResponse<BorderCrossing>>,
    IRequestHandler<
      FindBorderShipments,
      RequestResponse<List<BorderShipmentOption>>
    >,
    IRequestHandler<
      GetBorderAssignments,
      RequestResponse<List<BorderAssignmentOption>>
    >,
    IRequestHandler<CheckBorderCrossing, RequestResponse<List<ShipmentIssue>>>
{
  public async Task<RequestResponse<List<BorderPort>>> Handle(
    GetBorderPorts query,
    CancellationToken ct
  )
  {
    if (await BorderAccess.Actor(db, caller, roles, ct) is null)
      return RequestResponse<List<BorderPort>>.Fail("Access denied.", 403);
    return RequestResponse<List<BorderPort>>.Ok(BorderPorts.All.ToList());
  }

  public async Task<RequestResponse<List<BorderSummary>>> Handle(
    ListBorderCrossings query,
    CancellationToken ct
  )
  {
    if (await BorderAccess.Actor(db, caller, roles, ct) is null)
      return RequestResponse<List<BorderSummary>>.Fail("Access denied.", 403);
    if (query.Offset is < 0 or > 10000)
      return RequestResponse<List<BorderSummary>>.Fail(
        "Choose a supported page."
      );
    var rows = await db
      .BorderCrossings.AsNoTracking()
      .OrderByDescending(x => x.UpdatedAt)
      .ThenBy(x => x.Id)
      .Skip(query.Offset)
      .Take(25)
      .Select(x => new BorderSummary(
        x.Id,
        x.Reference,
        x.DestinationCountry,
        x.PortOfEntry,
        x.Revision
      ))
      .ToListAsync(ct);
    return RequestResponse<List<BorderSummary>>.Ok(rows);
  }

  public async Task<RequestResponse<BorderCrossing>> Handle(
    GetBorderCrossing query,
    CancellationToken ct
  )
  {
    if (await BorderAccess.Actor(db, caller, roles, ct) is null)
      return RequestResponse<BorderCrossing>.Fail("Access denied.", 403);
    await using var transaction = await db.Database.BeginTransactionAsync(
      IsolationLevel.Serializable,
      ct
    );
    var row = await db
      .BorderCrossings.AsNoTracking()
      .AsSplitQuery()
      .SingleOrDefaultAsync(x => x.Id == query.Id, ct);
    return row is null
      ? RequestResponse<BorderCrossing>.Fail("Crossing not found.", 404)
      : RequestResponse<BorderCrossing>.Ok(
        BorderMapping.Project(row, protection)
      );
  }

  public async Task<RequestResponse<List<BorderShipmentOption>>> Handle(
    FindBorderShipments query,
    CancellationToken ct
  )
  {
    if (await BorderAccess.Actor(db, caller, roles, ct) is null)
      return RequestResponse<List<BorderShipmentOption>>.Fail(
        "Access denied.",
        403
      );
    var term = query.Search?.Trim() ?? "";
    if (term.Length is < 1 or > 100)
      return RequestResponse<List<BorderShipmentOption>>.Fail(
        "Enter a load number, BOL or broker."
      );
    int.TryParse(term, out var number);
    var rows = await (
      from shipment in db.Shipments.AsNoTracking()
      join load in db.Dispatches.AsNoTracking()
        on shipment.LoadId equals load.Id
      where
        load.LoadNumber == number
        || shipment.BillOfLading.Contains(term)
        || load.CustomerName.Contains(term)
      orderby load.LoadNumber descending, shipment.Id
      select new BorderShipmentOption(
        shipment.Id,
        shipment.LoadId,
        load.LoadNumber,
        load.CustomerName,
        shipment.BillOfLading,
        shipment.Revision
      )
    )
      .Take(25)
      .ToListAsync(ct);
    return RequestResponse<List<BorderShipmentOption>>.Ok(rows);
  }

  public async Task<RequestResponse<List<BorderAssignmentOption>>> Handle(
    GetBorderAssignments query,
    CancellationToken ct
  )
  {
    if (await BorderAccess.Actor(db, caller, roles, ct) is null)
      return RequestResponse<List<BorderAssignmentOption>>.Fail(
        "Access denied.",
        403
      );
    var rows = await (
      from stop in db.ExecutionLegStops.AsNoTracking()
      join leg in db.ExecutionLegs.AsNoTracking()
        on stop.ExecutionLegId equals leg.Id
      where stop.DispatchId == query.LoadId
      orderby leg.RecordedAt, stop.Position
      select new BorderAssignmentOption(
        leg.Id,
        stop.Id,
        leg.Revision,
        stop.Job + " · " + stop.Name,
        leg.TruckId,
        leg.TrailerId,
        stop.HasDriverOverride ? stop.DriverId : leg.DriverId,
        stop.HasDriverOverride ? stop.CoDriverId : leg.CoDriverId
      )
    )
      .Take(100)
      .ToListAsync(ct);
    return RequestResponse<List<BorderAssignmentOption>>.Ok(rows);
  }

  public async Task<RequestResponse<List<ShipmentIssue>>> Handle(
    CheckBorderCrossing query,
    CancellationToken ct
  )
  {
    if (await BorderAccess.Actor(db, caller, roles, ct) is null)
      return RequestResponse<List<ShipmentIssue>>.Fail("Access denied.", 403);
    var error = BorderRules.Validate(query.Crossing);
    if (error is not null)
      return RequestResponse<List<ShipmentIssue>>.Fail(error);
    var issues = BorderRules.Missing(query.Crossing);
    if (
      query.Crossing.SourceLegId is { } legId
      && !await db.ExecutionLegs.AnyAsync(
        x => x.Id == legId && x.Revision == query.Crossing.SourceRevision,
        ct
      )
    )
      issues.Add(
        new(
          "Assignment",
          "The source assignment changed. Review the crossing resources."
        )
      );
    foreach (var shipment in query.Crossing.Shipments)
      if (
        !await db.Shipments.AnyAsync(
          x =>
            x.Id == shipment.ShipmentId
            && x.Revision == shipment.ShipmentRevision,
          ct
        )
      )
        issues.Add(
          new(
            $"Shipments.{shipment.Id}",
            "The source shipment changed. Review its saved version."
          )
        );
    return RequestResponse<List<ShipmentIssue>>.Ok(issues);
  }
}

internal static class BorderAccess
{
  public static async Task<Guid?> Actor(
    IAppDbContext db,
    ICurrentUser caller,
    IUserRoleService roles,
    CancellationToken ct
  )
  {
    if (
      !caller.IsAuthenticated
      || caller.IdentityUserId is not { } identity
      || await roles.GetAsync(identity, ct) != "Admin"
    )
      return null;
    return await db
      .Users.AsNoTracking()
      .Where(x => x.IdentityUserId == identity && x.IsActive)
      .Select(x => (Guid?)x.Id)
      .SingleOrDefaultAsync(ct);
  }
}
