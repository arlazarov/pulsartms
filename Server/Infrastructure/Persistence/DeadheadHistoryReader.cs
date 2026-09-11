using System.Linq.Expressions;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Models;
using Domain.Entities.Dispatch;
using Microsoft.EntityFrameworkCore;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Infrastructure.Persistence;

public sealed class DeadheadHistoryReader(AppDbContext db) : IDeadheadHistoryReader
{
  public async Task<IReadOnlyDictionary<Guid, DeadheadHistorySnapshot>> ReadAsync(IReadOnlyCollection<Guid> dispatchIds, CancellationToken ct)
  {
    if (dispatchIds.Count == 0) return new Dictionary<Guid, DeadheadHistorySnapshot>();
    var current = await db.Dispatches.AsNoTracking().Where(x => dispatchIds.Contains(x.Id)).Select(Projection).ToListAsync(ct);
    return await ReadLoadedAsync(current, ct);
  }

  public async Task<IReadOnlyDictionary<Guid, DeadheadHistorySnapshot>> ReadLoadedAsync(IReadOnlyCollection<Load> current, CancellationToken ct)
  {
    if (current.Count == 0) return new Dictionary<Guid, DeadheadHistorySnapshot>();
    var unknown = new HashSet<Guid>();
    var predecessorIds = current.ToDictionary(x => x.Id, _ => new List<Guid>());
    if (db.Database.IsNpgsql())
    {
      var candidates = await PostgresCandidates(current.Select(x => x.Id).ToArray()).ToListAsync(ct);
      foreach (var candidate in candidates)
      {
        if (candidate.HasUnknownStart && candidate.TruckId is { } truck) unknown.Add(truck);
        if (!candidate.HasUnknownStart && candidate.PreviousId is { } previous)
          predecessorIds[candidate.CurrentId].Add(previous);
      }
    }
    else
    {
      var truckIds = current.Where(x => x.TruckId.HasValue).Select(x => x.TruckId!.Value).Distinct().ToArray();
      unknown = (await Starts().Where(x => x.TruckId.HasValue && truckIds.Contains(x.TruckId.Value)
        && x.Status != "cancelled" && x.Date == null).Select(x => x.TruckId!.Value).Distinct().ToListAsync(ct)).ToHashSet();
      var eligible = current.Where(x => x.TruckId is { } truck && !unknown.Contains(truck)
        && (x.Stops.OrderBy(s => s.Sequence).FirstOrDefault()?.ScheduledDate ?? x.ShipDate) is not null).ToArray();
      // SQLite cannot translate a correlated top-two query to LATERAL; keep its test fallback bounded.
      foreach (var load in eligible)
      {
        var first = load.Stops.OrderBy(s => s.Sequence).FirstOrDefault();
        var date = first?.ScheduledDate ?? load.ShipDate;
        var time = first?.ScheduledTime ?? TimeOnly.MinValue;
        predecessorIds[load.Id] = await Starts()
          .Where(x => x.TruckId == load.TruckId && x.Id != load.Id && x.Status != "cancelled"
            && (x.Date < date || x.Date == date && x.Time <= time))
          .OrderByDescending(x => x.Date).ThenByDescending(x => x.Time).ThenBy(x => x.Id)
          .Take(2).Select(x => x.Id).ToListAsync(ct);
      }
    }
    var ids = predecessorIds.Values.SelectMany(x => x).Distinct().ToArray();
    var predecessors = await db.Dispatches.AsNoTracking().Where(x => ids.Contains(x.Id)).Select(Projection).ToDictionaryAsync(x => x.Id, ct);
    return current.ToDictionary(x => x.Id, x => new DeadheadHistorySnapshot(x,
      predecessorIds[x.Id].Where(predecessors.ContainsKey).Select(id => predecessors[id]).ToArray(),
      x.TruckId.HasValue && unknown.Contains(x.TruckId.Value)));
  }

  private IQueryable<Candidate> PostgresCandidates(Guid[] currentIds) => Starts().Where(x => currentIds.Contains(x.Id))
    .SelectMany(current => Starts().Where(previous => previous.TruckId == current.TruckId
        && previous.Id != current.Id && previous.Status != "cancelled"
        && (previous.Date < current.Date || previous.Date == current.Date && previous.Time <= current.Time))
      .OrderByDescending(x => x.Date).ThenByDescending(x => x.Time).ThenBy(x => x.Id).Take(2).DefaultIfEmpty(),
      (current, previous) => new Candidate { CurrentId = current.Id, TruckId = current.TruckId,
        PreviousId = previous == null ? null : previous.Id,
        HasUnknownStart = Starts().Any(x => x.TruckId == current.TruckId && x.Status != "cancelled" && x.Date == null) });

  private IQueryable<Start> Starts() => db.Dispatches.AsNoTracking().Select(x => new Start
  {
    Id = x.Id, TruckId = x.TruckId, Status = x.Status,
    Date = x.Stops.OrderBy(s => s.Sequence).Select(s => s.ScheduledDate).FirstOrDefault() ?? x.ShipDate,
    Time = x.Stops.OrderBy(s => s.Sequence).Select(s => s.ScheduledTime).FirstOrDefault() ?? TimeOnly.MinValue
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

  private static readonly Expression<Func<Load, Load>> Projection = load => new Load
  {
    Id = load.Id, TruckId = load.TruckId, Status = load.Status,
    ShipDate = load.ShipDate, DeliveryDate = load.DeliveryDate,
    Price = load.Price, Currency = load.Currency, LoadedMiles = load.LoadedMiles,
    Stops = load.Stops.Select(stop => new DispatchStop
    {
      Id = stop.Id, TruckId = stop.TruckId, Sequence = stop.Sequence, Job = stop.Job,
      ScheduledDate = stop.ScheduledDate, ScheduledTime = stop.ScheduledTime,
      Address = stop.Address, City = stop.City, Province = stop.Province,
      Country = stop.Country, ZipCode = stop.ZipCode, Latitude = stop.Latitude, Longitude = stop.Longitude,
      AddressVerifiedAt = stop.AddressVerifiedAt, SourceAddressJson = stop.SourceAddressJson, AddressRetryAfter = stop.AddressRetryAfter
    }).ToList()
  };
}
