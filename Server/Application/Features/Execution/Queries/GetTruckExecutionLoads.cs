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
  IReadOnlyCollection<Guid>? CandidateDispatchIds = null
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
    IReadOnlyCollection<Guid>? candidates,
    CancellationToken ct,
    IReadOnlyCollection<Guid>? executionLegIds = null,
    IReadOnlyCollection<Guid>? completedLegIds = null,
    IReadOnlyCollection<Guid>? truckIds = null
  )
  {
    var ownedQuery = db.LoadExecutionLegs.AsNoTracking();
    if (candidates is not null)
      ownedQuery = ownedQuery.Where(x => candidates.Contains(x.DispatchId));
    var owned = (
      await ownedQuery.Select(x => x.DispatchId).Distinct().ToListAsync(ct)
    ).ToHashSet();
    var completed = completedLegIds ?? [];
    var query = db
      .LoadExecutionLegs.AsNoTracking()
      .Where(x =>
        x.ExecutionLeg.Status == "active"
        || x.ExecutionLeg.Status == "planned"
        || x.ExecutionLeg.Status == "completed"
          && completed.Contains(x.ExecutionLegId)
      );
    if (truckId.HasValue)
      query = query.Where(x => x.ExecutionLeg.TruckId == truckId);
    if (truckIds is not null)
      query = query.Where(x => truckIds.Contains(x.ExecutionLeg.TruckId));
    if (executionLegIds is not null)
      query = query.Where(x => executionLegIds.Contains(x.ExecutionLegId));
    var links = await query
      .Include(x => x.ExecutionLeg)
      .OrderBy(x => x.ExecutionLeg.Status == "active" ? 0 : 1)
      .ThenBy(x => x.Sequence)
      .ThenBy(x => x.Id)
      .ToListAsync(ct);
    if (links.Count == 0)
      return new([], owned);
    var ids = links.Select(x => x.DispatchId).Distinct().ToArray();
    var loads = await db
      .Dispatches.AsNoTracking()
      .Where(x => ids.Contains(x.Id))
      .ToDictionaryAsync(x => x.Id, ct);
    var legs = links
      .Select(x => x.ExecutionLeg)
      .DistinctBy(x => x.Id)
      .ToArray();
    var snapshots = legs.ToDictionary(x => x.Id, ReadSnapshot);
    var legIds = legs.Select(x => x.Id).ToArray();
    var participants = await transfers.ForLegsAsync(legIds, ct);
    var visits = ExecutionTransfers.Project(legs, participants);
    var outgoing = participants.ToDictionary(x => x.OutgoingLegId);
    var incoming = participants.ToDictionary(x => x.IncomingLegId);
    var truckNames = await names.TrucksAsync(ct);
    var driverNames = await names.DriversAsync(ct);
    var trailerNames = await names.TrailersAsync(ct);
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
          Status = leg.Status switch
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
}
