using System.Diagnostics;
using Application.Diagnostics;
using Application.Features.Execution.Models;
using Application.Features.Execution.Services;
using Application.Reference;
using Domain.Entities.Dispatch;
using Domain.Entities.Execution;

namespace Application.Features.Execution.Queries;

public sealed record TruckExecutionLoads(
  IReadOnlyList<ExecutionLoadSnapshot> Loads,
  IReadOnlySet<Guid> OwnedDispatchIds
)
{
  internal IReadOnlyList<ExecutionLeg> Legs { get; init; } = [];
  internal IReadOnlyDictionary<
    Guid,
    ExecutionTransferVisit
  > Visits { get; init; } = new Dictionary<Guid, ExecutionTransferVisit>();
}

public sealed record GetTruckExecutionLoadsQuery(
  Guid TruckId,
  IReadOnlyCollection<Guid> CandidateDispatchIds
) : IRequest<TruckExecutionLoads>;

public sealed class GetTruckExecutionLoadsHandler(
  IAppDbContext db,
  FleetNames names,
  ActiveTransfers transfers
) : IRequestHandler<GetTruckExecutionLoadsQuery, TruckExecutionLoads>
{
  public Task<TruckExecutionLoads> Handle(
    GetTruckExecutionLoadsQuery request,
    CancellationToken ct
  ) =>
    ExecutionLoads.ReadAsync(
      db,
      names,
      transfers,
      request.TruckId,
      request.CandidateDispatchIds,
      ct
    );
}

public static class ExecutionLoads
{
  public static async Task<TruckExecutionLoads> ReadAsync(
    IAppDbContext db,
    FleetNames names,
    ActiveTransfers transfers,
    Guid? truckId,
    IReadOnlyCollection<Guid> candidates,
    CancellationToken ct,
    IReadOnlyCollection<Guid>? executionLegIds = null,
    IReadOnlyCollection<Guid>? completedLegIds = null,
    IReadOnlyCollection<Guid>? truckIds = null
  )
  {
    // One read of the link table where there were two. The rows that say
    // which dispatches execution owns and the rows this caller wants to see
    // come from the same table and overlap; each read of this database costs
    // a round trip whatever it asks for, and this runs twice in a single
    // request for upcoming loads.
    var wanted = candidates.ToHashSet();
    var completed = completedLegIds?.ToHashSet() ?? [];
    // Both truck filters are applied together where a caller gives both, so
    // they narrow to one set. Null stays null: the board reads every truck.
    var trucks = truckIds?.ToHashSet();
    if (truckId is { } single)
      trucks =
        trucks is null || trucks.Contains(single)
          ? [single]
          : new HashSet<Guid>();
    var legFilter = executionLegIds?.ToHashSet();
    var anyTruck = trucks is null;
    var truckSet = trucks ?? new HashSet<Guid>();
    var anyLeg = legFilter is null;
    var legSet = legFilter ?? new HashSet<Guid>();
    var started = Stopwatch.GetTimestamp();
    var rows = await db
      .LoadExecutionLegs.AsNoTracking()
      .Where(x =>
        wanted.Contains(x.DispatchId)
        || (
          (
            x.ExecutionLeg.Status == "active"
            || x.ExecutionLeg.Status == "planned"
            || x.ExecutionLeg.Status == "completed"
              && completed.Contains(x.ExecutionLegId)
          )
          && (anyTruck || truckSet.Contains(x.ExecutionLeg.TruckId))
          && (anyLeg || legSet.Contains(x.ExecutionLegId))
        )
      )
      .Include(x => x.ExecutionLeg)
      .OrderBy(x => x.ExecutionLeg.Status == "active" ? 0 : 1)
      .ThenBy(x => x.Sequence)
      .ThenBy(x => x.Id)
      .ToListAsync(ct);
    started = Mark("links", started);
    var owned = rows.Where(x => wanted.Contains(x.DispatchId))
      .Select(x => x.DispatchId)
      .ToHashSet();
    // The order the database returned is kept: filtering does not disturb it.
    var links = rows.Where(x =>
        (
          x.ExecutionLeg.Status == "active"
          || x.ExecutionLeg.Status == "planned"
          || x.ExecutionLeg.Status == "completed"
            && completed.Contains(x.ExecutionLegId)
        )
        && (anyTruck || truckSet.Contains(x.ExecutionLeg.TruckId))
        && (anyLeg || legSet.Contains(x.ExecutionLegId))
      )
      .ToList();
    if (links.Count == 0)
      return new([], owned);
    var ids = links.Select(x => x.DispatchId).Distinct().ToArray();
    var loads = await db
      .Dispatches.AsNoTracking()
      .Where(x => ids.Contains(x.Id))
      .ToDictionaryAsync(x => x.Id, ct);
    started = Mark("loads", started);
    var legs = links
      .Select(x => x.ExecutionLeg)
      .DistinctBy(x => x.Id)
      .ToArray();
    var snapshots = legs.ToDictionary(x => x.Id, ReadSnapshot);
    var legIds = legs.Select(x => x.Id).ToArray();
    var participants = await transfers.ForLegsAsync(legIds, ct);
    started = Mark("transfers", started);
    var visits = ExecutionTransfers.Project(legs, participants);
    var outgoing = participants.ToDictionary(x => x.OutgoingLegId);
    var incoming = participants.ToDictionary(x => x.IncomingLegId);
    var truckNames = await names.TrucksAsync(ct);
    var driverNames = await names.DriversAsync(ct);
    var trailerNames = await names.TrailersAsync(ct);
    Mark("names", started);
    var result = new List<ExecutionLoadSnapshot>();
    foreach (var link in links)
    {
      if (!loads.TryGetValue(link.DispatchId, out var source))
        continue;
      var leg = link.ExecutionLeg;
      var stops = snapshots[leg.Id].Select(ExecutionSnapshots.Copy).ToList();
      foreach (var stop in stops)
      {
        stop.TruckId = leg.TruckId;
        stop.TrailerId = leg.TrailerId;
        stop.TruckNumber = truckNames.GetValueOrDefault(leg.TruckId, "");
        stop.DriverName = stop.DriverId is { } driver
          ? driverNames.GetValueOrDefault(driver, "")
          : "";
        stop.CoDriverName = stop.CoDriverId is { } coDriver
          ? driverNames.GetValueOrDefault(coDriver, "")
          : "";
        stop.TrailerNumber = leg.TrailerId is { } trailer
          ? trailerNames.GetValueOrDefault(trailer, "")
          : "";
        if (visits.TryGetValue(stop.Id, out var visit))
          ExecutionSnapshots.ApplyActual(stop, visit);
      }
      if (stops.Count > 0 && outgoing.TryGetValue(leg.Id, out var release))
        stops[^1].AwaitingHandoff = !release.ReleasedBy.HasValue;
      if (stops.Count > 0 && incoming.TryGetValue(leg.Id, out var receive))
        stops[0].AwaitingHandoff = !receive.ReceivedBy.HasValue;
      var captured = ExecutionLoadProjection.Capture(source, leg, stops);
      var load = captured with
      {
        Work = captured.Work with
        {
          // The leg says how far along the work is; the load says whether
          // there is work at all. A load the source has cancelled keeps
          // saying so, or its open leg would answer for it - which is how
          // 11005 kept a cancelled trip on the map.
          Status = source.Status is "cancelled" or "canceled"
            ? source.Status
            : leg.Status switch
            {
              "active" => "in_transit",
              "completed" => "completed",
              _ => "assigned",
            },
          TruckNumber = truckNames.GetValueOrDefault(leg.TruckId, ""),
        },
        Details = captured.Details with
        {
          DriverName = captured.Work.DriverId is { } assignedDriver
            ? driverNames.GetValueOrDefault(assignedDriver, "")
            : "",
          TrailerNumber = leg.TrailerId is { } assignedTrailer
            ? trailerNames.GetValueOrDefault(assignedTrailer, "")
            : "",
        },
      };
      result.Add(load);
    }
    return new(result, owned) { Legs = legs, Visits = visits };
  }

  private static List<DispatchStop> ReadSnapshot(ExecutionLeg leg)
  {
    var stops = ExecutionStopRows.Read(leg);
    return
      stops.Count is >= 1 and <= 49
      && stops.Select(x => x.Id).Distinct().Count() == stops.Count
      ? stops
      : [];
  }

  private static long Mark(string stage, long since)
  {
    PerformanceStages.Elapsed("execution-loads", stage, since);
    return Stopwatch.GetTimestamp();
  }
}
