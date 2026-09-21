using System.Collections.Immutable;
using System.Linq.Expressions;
using Application.Features.Routing.Interfaces;
using Domain.Entities.Dispatch;
using Domain.Models.Routing;
using Microsoft.EntityFrameworkCore;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Infrastructure.Persistence;

public sealed class DeadheadHistoryReader(AppDbContext db)
  : IDeadheadHistoryReader
{
  public async Task<IReadOnlyDictionary<Guid, DeadheadHistorySource>> ReadAsync(
    IReadOnlyCollection<Guid> dispatchIds,
    CancellationToken ct
  )
  {
    if (dispatchIds.Count == 0)
      return new Dictionary<Guid, DeadheadHistorySource>();
    var current = await db
      .Dispatches.AsNoTracking()
      .Where(x => dispatchIds.Contains(x.Id))
      .Select(Projection)
      .ToListAsync(ct);
    return await ReadLoadedAsync(current, ct);
  }

  public async Task<
    IReadOnlyDictionary<Guid, DeadheadHistorySource>
  > ReadLoadedAsync(
    IReadOnlyCollection<RouteWorkSnapshot> current,
    CancellationToken ct
  )
  {
    if (current.Count == 0)
      return new Dictionary<Guid, DeadheadHistorySource>();
    current = current.Select(TruckItinerary).ToArray();
    var unknown = new HashSet<Guid>();
    var predecessorIds = current.ToDictionary(x => x.Id, _ => new List<Guid>());
    if (db.Database.IsNpgsql())
    {
      var candidates = await PostgresCandidates(current).ToListAsync(ct);
      foreach (var candidate in candidates)
      {
        if (candidate.HasUnknownStart && candidate.TruckId is { } truck)
          unknown.Add(truck);
        if (!candidate.HasUnknownStart && candidate.PreviousId is { } previous)
          predecessorIds[candidate.CurrentId].Add(previous);
      }
    }
    else
    {
      var truckIds = current
        .Where(x => x.TruckId.HasValue)
        .Select(x => x.TruckId!.Value)
        .Distinct()
        .ToArray();
      unknown = (
        await Starts()
          .Where(x =>
            x.TruckId.HasValue
            && truckIds.Contains(x.TruckId.Value)
            && x.Status != "cancelled"
            && x.Date == null
          )
          .Select(x => x.TruckId!.Value)
          .Distinct()
          .ToListAsync(ct)
      ).ToHashSet();
      var eligible = current
        .Where(x =>
          x.TruckId is { } truck
          && !unknown.Contains(truck)
          && (
            x.Stops.OrderBy(s => s.Sequence).FirstOrDefault()?.ScheduledDate
            ?? x.ShipDate
          )
            is not null
        )
        .ToArray();
      // SQLite cannot translate a correlated top-two query to LATERAL; keep its
      // test fallback bounded.
      foreach (var load in eligible)
      {
        var first = load.Stops.OrderBy(s => s.Sequence).FirstOrDefault();
        var date = first?.ScheduledDate ?? load.ShipDate;
        var time = first?.ScheduledTime ?? TimeOnly.MinValue;
        predecessorIds[load.Id] = await Starts()
          .Where(x =>
            x.TruckId == load.TruckId
            && x.Id != load.Id
            && x.Status != "cancelled"
            && (x.Date < date || x.Date == date && x.Time <= time)
          )
          .OrderByDescending(x => x.Date)
          .ThenByDescending(x => x.Time)
          .ThenBy(x => x.Id)
          .Take(2)
          .Select(x => x.Id)
          .ToListAsync(ct);
      }
    }
    var ids = predecessorIds.Values.SelectMany(x => x).Distinct().ToArray();
    var predecessors = await db
      .Dispatches.AsNoTracking()
      .Where(x => ids.Contains(x.Id))
      .Select(Projection)
      .ToDictionaryAsync(x => x.Id, ct);
    return current.ToDictionary(
      x => x.Id,
      x => new DeadheadHistorySource(
        x,
        predecessorIds[x.Id]
          .Where(predecessors.ContainsKey)
          .Select(id => TruckItinerary(predecessors[id]))
          .ToImmutableArray(),
        x.TruckId.HasValue && unknown.Contains(x.TruckId.Value)
      )
    );
  }

  private IQueryable<Candidate> PostgresCandidates(
    IReadOnlyCollection<RouteWorkSnapshot> loads
  )
  {
    var current = loads
      .Select(load => new CurrentStart
      {
        Id = load.Id,
        TruckId = load.TruckId,
        Date =
          load.Stops.OrderBy(x => x.Sequence).FirstOrDefault()?.ScheduledDate
          ?? load.ShipDate,
        Time =
          load.Stops.OrderBy(x => x.Sequence).FirstOrDefault()?.ScheduledTime
          ?? TimeOnly.MinValue,
      })
      .ToArray();
    var ids = current.Select(x => x.Id).ToArray();
    var trucks = current.Select(x => x.TruckId).ToArray();
    var dates = current.Select(x => x.Date).ToArray();
    var times = current.Select(x => x.Time).ToArray();
    // Seed the batch with captured inputs instead of rereading current loads.
    return db
      .Database.SqlQuery<CurrentStart>(
        $"""
        SELECT * FROM unnest({ids}, {trucks}, {dates}, {times})
          AS captured("Id", "TruckId", "Date", "Time")
        """
      )
      .SelectMany(
        current =>
          Starts()
            .Where(previous =>
              current.TruckId != null
              && current.Date != null
              && previous.TruckId == current.TruckId
              && previous.Id != current.Id
              && previous.Status != "cancelled"
              && (
                previous.Date < current.Date
                || previous.Date == current.Date
                  && previous.Time <= current.Time
              )
            )
            .OrderByDescending(x => x.Date)
            .ThenByDescending(x => x.Time)
            .ThenBy(x => x.Id)
            .Take(2)
            .DefaultIfEmpty(),
        (current, previous) =>
          new Candidate
          {
            CurrentId = current.Id,
            TruckId = current.TruckId,
            PreviousId = previous == null ? null : previous.Id,
            HasUnknownStart = Starts()
              .Any(x =>
                x.TruckId == current.TruckId
                && x.Status != "cancelled"
                && x.Date == null
              ),
          }
      );
  }

  private sealed class CurrentStart
  {
    public Guid Id { get; init; }
    public Guid? TruckId { get; init; }
    public DateOnly? Date { get; init; }
    public TimeOnly Time { get; init; }
  }

  private IQueryable<Start> Starts() =>
    db
      .Dispatches.AsNoTracking()
      .Select(load => new
      {
        Load = load,
        StartSequence = load.PlanningFromStopId != null
          ? load.Stops.Where(s => s.Id == load.PlanningFromStopId)
            .Select(s => (int?)s.Sequence)
            .FirstOrDefault() ?? int.MaxValue
          : load.Stops.Where(s =>
              s.ManualStateAfter != "No truck"
              && (
                s.TruckId != null
                || s.TruckNumber != ""
                || s.ManualStateAfter != null
              )
            )
            .OrderBy(s => s.Sequence)
            .Select(s => (int?)s.Sequence)
            .FirstOrDefault()
            ?? (
              load.Stops.Any(s => s.ManualStateAfter == "No truck")
                ? int.MaxValue
                : int.MinValue
            ),
      })
      .Select(x => new Start
      {
        Id = x.Load.Id,
        TruckId =
          x.Load.PlanningTruckId
          ?? x.Load.TruckId
          ?? x.Load.Stops.OrderBy(s => s.Sequence)
            .Where(s => s.TruckId != null)
            .Select(s => s.TruckId)
            .FirstOrDefault(),
        Status = x.Load.Status,
        Date =
          x.Load.Stops.Where(s => s.Sequence >= x.StartSequence)
            .OrderBy(s => s.Sequence)
            .Select(s => s.ScheduledDate)
            .FirstOrDefault() ?? x.Load.ShipDate,
        Time =
          x.Load.Stops.Where(s => s.Sequence >= x.StartSequence)
            .OrderBy(s => s.Sequence)
            .Select(s => s.ScheduledTime)
            .FirstOrDefault() ?? TimeOnly.MinValue,
      });

  private sealed class Candidate
  {
    public Guid CurrentId { get; init; }
    public Guid? TruckId { get; init; }
    public Guid? PreviousId { get; init; }
    public bool HasUnknownStart { get; init; }
  }

  private sealed class Start
  {
    public Guid Id { get; init; }
    public Guid? TruckId { get; init; }
    public string? Status { get; init; }
    public DateOnly? Date { get; init; }
    public TimeOnly Time { get; init; }
  }

  private static RouteWorkSnapshot TruckItinerary(RouteWorkSnapshot work)
  {
    if (work.ExecutionLegId.HasValue)
      return work;
    var path = TruckPath.Resolve(
      work.TruckId,
      work.TruckNumber,
      work.PlanningTruckId,
      work.PlanningFromStopId,
      work.Stops,
      (stop, job, state) => stop with { Job = job, StateAfter = state }
    );
    return work with
    {
      TruckId = path.TruckId,
      TruckNumber = path.TruckNumber,
      Stops = path.Stops.ToImmutableArray(),
    };
  }

  private static readonly Expression<Func<Load, RouteWorkSnapshot>> Projection =
    load => new RouteWorkSnapshot(
      load.Id,
      load.TruckId,
      load.TruckNumber,
      load.Status,
      null,
      null,
      0,
      load.PlanningTruckId,
      load.PlanningFromStopId,
      load.PlanningAssignmentRevision,
      load.RouteChoiceRevision,
      load.ShipDate,
      load.DeliveryDate,
      load.Price,
      load.Currency,
      load.LoadedMiles,
      load.Stops.Select(stop => new RouteWorkStop(
          stop.Id,
          stop.TruckId,
          stop.TruckNumber,
          stop.Sequence,
          stop.Job,
          stop.ManualAction,
          stop.ManualStateAfter,
          "Unknown",
          stop.OperationRevision,
          false,
          false,
          stop.ScheduledDate,
          stop.ScheduledTime,
          stop.PickedUpAt,
          stop.DeliveredAt,
          stop.DepartedAt,
          stop.ManualCompletedAt,
          stop.CompletionOverride,
          stop.ManualCompletionRevision,
          stop.Name,
          stop.Address,
          stop.City,
          stop.Province,
          stop.Country,
          stop.ZipCode,
          stop.Latitude,
          stop.Longitude,
          stop.AddressVerifiedAt,
          stop.AddressRetryAfter,
          stop.SourceAddressJson
        )
        {
          DriverId = stop.DriverId,
          CoDriverId = stop.CoDriverId,
          TrailerId = stop.TrailerId,
          ArrivedAt = stop.ArrivedAt,
          ManualCompletedBy = stop.ManualCompletedBy,
          ScheduledDate2 = stop.ScheduledDate2,
          ScheduledTime2 = stop.ScheduledTime2,
          IsWindow = stop.IsWindow,
          AppointmentTimeZoneId = stop.AppointmentTimeZoneId,
          Commodity = stop.Commodity,
          Notes = stop.Notes,
        })
        .ToImmutableArray()
    )
    {
      LoadNumber = load.LoadNumber,
      DriverId = load.DriverId,
      TrailerId = load.TrailerId,
    };
}
