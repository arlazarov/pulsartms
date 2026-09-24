using Application.Features.Execution.Models;
using Application.Features.Execution.Services;
using Application.Models;
using Application.Reference;

namespace Application.Features.Execution.Queries;

// What a driver is driving: the trucks of their planned and active
// execution legs, as driver or co-driver, and only when there are none, the
// truck the fleet assigns them (one driver per truck). One truck: its
// current and upcoming loads as the Dispatch board reads them
// (ExecutionWorkReader), so the two never disagree. Several trucks: all are
// listed and no load is chosen, since choosing one would be a guess. No
// driver: nothing to read.
public sealed record GetDriverWorkQuery(Guid? DriverId)
  : IRequest<RequestResponse<DriverWork>>;

public static class DriverWorkStates
{
  public const string Unmatched = "unmatched";
  public const string NoTruck = "no-truck";
  public const string OneTruck = "one-truck";
  public const string SeveralTrucks = "several-trucks";
}

public sealed record DriverWork(
  string State,
  IReadOnlyList<DriverTruck> Trucks,
  IReadOnlyList<DriverLoad> Loads
);

// Role: driver or co-driver on an execution leg, or assigned in the fleet.
public sealed record DriverTruck(Guid Id, string Number, string Role);

public sealed record DriverLoad(
  Guid Id,
  int LoadNumber,
  string CustomerName,
  string? Status,
  IReadOnlyList<string> Places
);

public sealed class DriverWorkHandler(
  IAppDbContext db,
  ICurrentUser caller,
  IUserRoleService roles,
  FleetNames names,
  ActiveTransfers transfers,
  TimeProvider clock
) : IRequestHandler<GetDriverWorkQuery, RequestResponse<DriverWork>>
{
  public const int MaximumLoads = 5;
  private static readonly string[] Live = ["planned", "active"];

  public async Task<RequestResponse<DriverWork>> Handle(
    GetDriverWorkQuery request,
    CancellationToken ct
  )
  {
    if (await ExecutionCommandSupport.ActorAsync(db, caller, roles, ct) is null)
      return RequestResponse<DriverWork>.Fail("Access denied.", 403);
    if (request.DriverId is not { } driver)
      return Ok(DriverWorkStates.Unmatched, [], []);
    var trucks = await TrucksAsync(driver, ct);
    if (trucks.Count != 1)
      return Ok(
        trucks.Count == 0
          ? DriverWorkStates.NoTruck
          : DriverWorkStates.SeveralTrucks,
        trucks,
        []
      );
    var work = await ExecutionWorkReader.ReadAsync(
      db,
      DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime),
      names,
      transfers,
      trucks[0].Id,
      includePlanned: true,
      includeOverdue: false,
      ct
    );
    return Ok(
      DriverWorkStates.OneTruck,
      trucks,
      [
        .. work.SelectMany(x => x.Loads)
          .Take(MaximumLoads)
          .Select(x => new DriverLoad(
            x.Id,
            x.LoadNumber,
            x.CustomerName,
            x.ExecutionStatus,
            [.. x.Visits.Select(v => v.City).Where(c => c.Length > 0)]
          )),
      ]
    );

    static RequestResponse<DriverWork> Ok(
      string state,
      IReadOnlyList<DriverTruck> trucks,
      IReadOnlyList<DriverLoad> loads
    ) => RequestResponse<DriverWork>.Ok(new(state, trucks, loads));
  }

  private async Task<IReadOnlyList<DriverTruck>> TrucksAsync(
    Guid driver,
    CancellationToken ct
  )
  {
    var legs = await db
      .ExecutionLegs.AsNoTracking()
      .Where(x =>
        Live.Contains(x.Status)
        && (x.DriverId == driver || x.CoDriverId == driver)
      )
      .Select(x => new
      {
        x.TruckId,
        Role = x.DriverId == driver ? "driver" : "co-driver",
        Number = db
          .Trucks.Where(t => t.Id == x.TruckId)
          .Select(t => t.UnitNumber)
          .FirstOrDefault(),
      })
      .ToListAsync(ct);
    if (legs.Count > 0)
      return
      [
        .. legs.GroupBy(x => x.TruckId)
          .Select(x => new DriverTruck(
            x.Key,
            x.First().Number ?? "",
            x.Any(l => l.Role == "driver") ? "driver" : "co-driver"
          ))
          .OrderBy(x => x.Number),
      ];
    return await db
      .Trucks.AsNoTracking()
      .Where(x => x.DriverId == driver)
      .OrderBy(x => x.UnitNumber)
      .Select(x => new DriverTruck(x.Id, x.UnitNumber, "assigned"))
      .ToListAsync(ct);
  }
}
