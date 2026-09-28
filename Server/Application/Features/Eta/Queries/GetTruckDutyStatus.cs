using Application.Features.Eta.Services;
using Application.Models;
using Domain.Models.Eta;

namespace Application.Features.Eta.Queries;

// The truck panel's duty reading for the driver the fleet assigns the
// truck: the status and how long it has lasted, the ongoing rest and when
// it completes a daily rest, and the cycle reset where the ruleset is
// known - with or without a route (DriverDutyReader).
public sealed record GetTruckDutyStatusQuery(Guid TruckId)
  : IRequest<RequestResponse<TruckDutyStatusView>>;

public sealed record TruckDutyStatusView(
  Guid TruckId,
  Guid? DriverId,
  DriverDutyStatus? Duty
);

public sealed class GetTruckDutyStatusHandler(
  IAppDbContext db,
  DriverDutyReader duty
)
  : IRequestHandler<
    GetTruckDutyStatusQuery,
    RequestResponse<TruckDutyStatusView>
  >
{
  public async Task<RequestResponse<TruckDutyStatusView>> Handle(
    GetTruckDutyStatusQuery request,
    CancellationToken ct
  )
  {
    var truck = await db
      .Trucks.AsNoTracking()
      .Where(x => x.Id == request.TruckId)
      .Select(x => new { x.DriverId })
      .SingleOrDefaultAsync(ct);
    if (truck is null)
      return RequestResponse<TruckDutyStatusView>.Fail("Truck not found.", 404);
    return RequestResponse<TruckDutyStatusView>.Ok(
      new(
        request.TruckId,
        truck.DriverId,
        truck.DriverId is { } driver
          ? await duty.ReadAsync(driver, request.TruckId, ct)
          : null
      )
    );
  }
}
