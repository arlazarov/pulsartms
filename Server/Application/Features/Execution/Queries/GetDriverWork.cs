using Application.Features.Execution.Models;
using Application.Features.Execution.Services;
using Application.Features.Routing.Services.Routes;
using Application.Models;
using Application.Reference;
using Domain.Models.Execution;

namespace Application.Features.Execution.Queries;

// What a driver is driving: the trucks of their planned and active
// execution legs, as driver or co-driver, and only when there are none, the
// truck the fleet assigns them (one driver per truck). One truck: its
// loads as the Dispatch board reads them (ExecutionWorkReader), each placed
// as the board places it (TruckPlanningInputs.Placements, WorkPlacements):
// the current load first, then the work after it, then work planning has
// passed without a delivery, shown with its conflict rather than hidden. A
// load read at another accepted revision is stale, never current. Several
// trucks: all are listed and no load is chosen, since choosing one would be
// a guess. No driver: nothing to read.
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
)
{
  // Loads beyond MaximumLoads, and how many of them carry a conflict: not
  // listed, but never silently absent.
  public int OmittedLoads { get; init; }
  public int OmittedConflicts { get; init; }
}

// Role: driver or co-driver on an execution leg, or assigned in the fleet.
public sealed record DriverTruck(Guid Id, string Number, string Role);

// Places: the stops' cities, kept for earlier clients; Stops names them.
public sealed record DriverLoad(
  Guid Id,
  int LoadNumber,
  string CustomerName,
  string? Status,
  IReadOnlyList<string> Places
)
{
  public string OrderNumber { get; init; } = "";
  public IReadOnlyList<DriverStop> Stops { get; init; } = [];

  // As on the board: DispatchResponse.WorkPhase and WorkConflict.
  public string? Phase { get; init; }
  public string? Conflict { get; init; }
}

public sealed record DriverStop(string Name, string City);

public sealed class DriverWorkHandler(
  IAppDbContext db,
  ICurrentUser caller,
  IUserRoleService roles,
  FleetNames names,
  ActiveTransfers transfers,
  TimeProvider clock,
  TruckPlanningInputsReader planning
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
    var inputs = await planning.ReadAsync(trucks[0].Id, ct, includeHos: false);
    var placed = DriverWorkOrder
      .Apply([.. work.SelectMany(x => x.Loads)], inputs)
      .ToList();
    var omitted = placed.Skip(MaximumLoads).ToList();
    var listed = new DriverWork(
      DriverWorkStates.OneTruck,
      trucks,
      [
        .. placed
          .Take(MaximumLoads)
          .Select(x => new DriverLoad(
            x.Load.Id,
            x.Load.LoadNumber,
            x.Load.CustomerName,
            x.Load.ExecutionStatus,
            [.. x.Load.Visits.Select(v => v.City).Where(c => c.Length > 0)]
          )
          {
            OrderNumber = x.Load.OrderNumber,
            Stops =
            [
              .. x.Load.Visits.Select(v => new DriverStop(v.Name, v.City)),
            ],
            Phase = x.Phase,
            Conflict = x.Conflict,
          }),
      ]
    )
    {
      OmittedLoads = omitted.Count,
      OmittedConflicts = omitted.Count(x => x.Conflict is not null),
    };
    return RequestResponse<DriverWork>.Ok(listed);

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

// A driver's loads as the board places them: the current one, work
// planning has passed without a delivery (its conflict shown, so a limit
// on the list cuts the least urgent last), the work after the current,
// then loads it does not place - each group in the board's order.
// Board rows are work not yet delivered (ExecutionWorkReader), so a passed
// row is a conflict here; the board itself also checks LoadCompletion.
internal static class DriverWorkOrder
{
  public static IEnumerable<(
    WorkLoadReference Load,
    string? Phase,
    string? Conflict
  )> Apply(IReadOnlyList<WorkLoadReference> loads, TruckPlanningInputs? inputs)
  {
    var placements = inputs?.Placements();
    return loads
      .Select(load =>
      {
        var phase = WorkPlacements.Phase(
          placements,
          new(load.Id, load.ExecutionLegId),
          load.AcceptedRevision
        );
        return (load, phase, WorkPlacements.Conflict(phase, completed: false));
      })
      .OrderBy(x =>
        x.phase switch
        {
          "current" => 0,
          "earlier" => 1,
          "next" => 2,
          "upcoming" => 3,
          _ => 4,
        }
      );
  }
}
