using Application.Features.Dispatch.Models;
using Application.Features.Mileage.Models;
using Application.Features.Mileage.Services;
using Application.Features.Routing.Services.Deadheads;
using Application.Models;

namespace Application.Features.Mileage.Queries;

public sealed record GetDispatchMileageQuery(Guid DispatchId)
  : IRequest<RequestResponse<DispatchMileageBreakdown>>;

public sealed class GetDispatchMileageHandler(
  IAppDbContext db,
  ICurrentUser caller,
  IUserRoleService roles,
  DeadheadService deadheads
)
  : IRequestHandler<
    GetDispatchMileageQuery,
    RequestResponse<DispatchMileageBreakdown>
  >
{
  private const int MaximumRows = 200;

  public async Task<RequestResponse<DispatchMileageBreakdown>> Handle(
    GetDispatchMileageQuery request,
    CancellationToken ct
  )
  {
    if (await MileageAccess.ActorAsync(db, caller, roles, false, ct) is null)
      return RequestResponse<DispatchMileageBreakdown>.Fail(
        "Access denied.",
        403
      );
    var id = request.DispatchId;
    var load = await db
      .Dispatches.AsNoTracking()
      .Where(x => x.Id == id)
      .Select(x => new
      {
        x.Id,
        x.LoadNumber,
        x.LoadedMiles,
        x.TruckId,
        x.DriverId,
        x.TrailerId,
        x.LastSyncedAt,
        From = x
          .Stops.OrderBy(s => s.Sequence)
          .Select(s => s.Address)
          .FirstOrDefault(),
        To = x
          .Stops.OrderByDescending(s => s.Sequence)
          .Select(s => s.Address)
          .FirstOrDefault(),
      })
      .SingleOrDefaultAsync(ct);
    if (load is null)
      return RequestResponse<DispatchMileageBreakdown>.Fail(
        "Dispatch not found.",
        404
      );
    var movements = await db
      .Movements.AsNoTracking()
      .Where(x => x.AllocatedDispatchId == id && !x.PlannedSuperseded)
      .OrderBy(x => x.RecordedAt)
      .ThenBy(x => x.Id)
      .Take(MaximumRows + 1)
      .ToListAsync(ct);
    var truncated = movements.Count > MaximumRows;
    var rows = movements
      .Take(MaximumRows)
      .Select(MileageMovementView.From)
      .ToList();
    var native = await db
      .LoadExecutionLegs.AsNoTracking()
      .AnyAsync(x => x.DispatchId == id, ct);
    var hasLedger =
      rows.Count > 0
      || await db
        .Movements.AsNoTracking()
        .AnyAsync(
          x =>
            x.PreviousDispatchId == id
            || x.NextDispatchId == id
            || x.CarriedDispatchId == id,
          ct
        );
    if (!native && !hasLedger)
    {
      var item = new DispatchResponse
      {
        Id = id,
        LoadedMiles = load.LoadedMiles,
      };
      await deadheads.ReadAsync([item], ct);
      var saved = await db
        .DispatchDeadheads.AsNoTracking()
        .Where(x => x.DispatchId == id && x.ExecutionLegId == null)
        .Select(x => new
        {
          x.Id,
          x.PreviousDispatchId,
          x.CalculatedAt,
        })
        .SingleOrDefaultAsync(ct);
      rows.Add(
        new(
          null,
          0,
          load.TruckId,
          load.DriverId,
          null,
          load.TrailerId,
          "delivery",
          "loaded",
          null,
          null,
          id,
          id,
          "carried",
          "imported-planned-loaded-distance",
          0,
          false,
          "imported-loaded-distance",
          load.LoadedMiles,
          null,
          load.LastSyncedAt,
          null,
          false
        )
        {
          FromLocation = load.From ?? "",
          ToLocation = load.To ?? "",
          PlannedSource = "imported-loaded-distance",
          PlannedSourceReference = id.ToString(),
        }
      );
      rows.Add(
        new(
          null,
          0,
          load.TruckId,
          load.DriverId,
          null,
          load.TrailerId,
          "pickup-approach",
          "empty",
          saved?.PreviousDispatchId,
          id,
          null,
          id,
          "next",
          "saved-planned-pickup-approach",
          0,
          false,
          "saved-deadhead",
          item.EmptyMiles,
          null,
          saved?.CalculatedAt,
          null,
          false
        )
        {
          FromLocation = "Previous delivery",
          ToLocation = load.From ?? "",
          PlannedSource = "saved-deadhead",
          PlannedSourceReference = saved?.Id.ToString(),
        }
      );
    }
    var details = await MileageMovementView.WithNumbersAsync(db, rows, ct);
    var gaps = await db
      .MileageCaptureGaps.AsNoTracking()
      .Where(gap =>
        db.LoadExecutionLegs.Any(link =>
          link.DispatchId == id
          && (
            link.ExecutionLegId == gap.ExecutionLegId
            || gap.ExecutionLegId == null
              && link.ExecutionLeg.TruckId == gap.TruckId
              && link.ExecutionLeg.RecordedAt <= gap.EndedAt
              && (
                link.ExecutionLeg.CompletedAt == null
                || link.ExecutionLeg.CompletedAt >= gap.StartedAt
              )
          )
        )
      )
      .OrderByDescending(x => x.EndedAt)
      .Take(100)
      .Select(x => new MileageGapRow(
        x.Id,
        x.TruckId,
        x.StartedAt,
        x.EndedAt,
        x.Reason
      ))
      .ToListAsync(ct);
    var pending = await db
      .OdometerIntervals.AsNoTracking()
      .Where(interval =>
        interval.Status == "pending"
        && db.LoadExecutionLegs.Any(link =>
          link.DispatchId == id
          && link.ExecutionLeg.TruckId == interval.TruckId
          && link.ExecutionLeg.RecordedAt <= interval.EndedAt
          && (
            link.ExecutionLeg.CompletedAt == null
            || link.ExecutionLeg.CompletedAt >= interval.StartedAt
          )
        )
      )
      .Take(10_001)
      .CountAsync(ct);
    return RequestResponse<DispatchMileageBreakdown>.Ok(
      new(
        id,
        load.LoadNumber,
        MileageSummation.Sum(details, actual: false, truncated),
        MileageSummation.Sum(details, actual: true, truncated),
        details,
        truncated
      )
      {
        ActualIsPartial = native || details.Any(x => x.Origin == "samsara-obd"),
        CaptureGaps = gaps,
        PendingObservedIntervals = pending,
      }
    );
  }
}
