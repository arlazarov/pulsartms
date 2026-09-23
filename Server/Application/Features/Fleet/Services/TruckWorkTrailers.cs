using Domain.Rules.Fleet;

namespace Application.Features.Fleet.Services;

// What each truck's current work says its trailer is, in two queries for
// the whole fleet. An active execution leg is the accepted decision and
// answers alone. Without one, an in-transit imported load answers at its
// first unfinished stop: that stop's trailer, else the load's, by id or,
// before the import resolved it, by number. Two current loads that name
// different trailers answer nothing.
internal static class TruckWorkTrailers
{
  // The answer per truck, and the numbers current loads name that no
  // trailer has yet, with the source that named them.
  public sealed record Reading(
    Dictionary<Guid, WorkTrailer> Trucks,
    IReadOnlyList<(string Source, string Number)> Unknown
  );

  public static async Task<Reading> ReadAsync(
    IAppDbContext db,
    CancellationToken ct
  )
  {
    var legs = await db
      .ExecutionLegs.AsNoTracking()
      .Where(x => x.Status == "active")
      .Select(x => new { x.TruckId, x.TrailerId })
      .ToListAsync(ct);
    var loads = await db
      .Dispatches.AsNoTracking()
      .Where(x =>
        x.Status == "in_transit"
        && !db.LoadExecutionLegs.Any(link => link.DispatchId == x.Id)
      )
      .Select(x => new
      {
        x.PlanningTruckId,
        x.TruckId,
        x.TrailerId,
        x.TrailerNumber,
        Source = db
          .DispatchSourceLinks.Where(link => link.DispatchId == x.Id)
          .Select(link => link.Provider)
          .FirstOrDefault(),
        Stops = x.Stops.OrderBy(s => s.Sequence).ToList(),
      })
      .ToListAsync(ct);
    var numbers = await db
      .Trailers.AsNoTracking()
      .Select(x => new { x.Id, x.UnitNumber })
      .ToListAsync(ct);
    var byNumber = numbers
      .Select(x => (x.Id, Unit: TrailerUnits.Normalize(x.UnitNumber)))
      .Where(x => x.Unit is not null)
      .GroupBy(x => x.Unit!)
      .Where(x => x.Count() == 1)
      .ToDictionary(x => x.Key, x => x.Single().Id, StringComparer.Ordinal);
    Guid? Named(Guid? id, string? number) =>
      id
      ?? (
        TrailerUnits.Normalize(number) is { } unit
        && byNumber.TryGetValue(unit, out var known)
          ? known
          : null
      );

    var result = new Dictionary<Guid, WorkTrailer>();
    foreach (var truck in legs.GroupBy(x => x.TruckId))
      result[truck.Key] = Answer(
        truck.Select(x => x.TrailerId),
        TruckTrailerSources.Execution
      );
    var fromLoads = new List<(Guid Truck, Guid? Trailer)>();
    var unknown = new List<(string Source, string Number)>();
    foreach (var load in loads)
    {
      var stop = load.Stops.FirstOrDefault(x => !x.IsCompleted);
      if (stop is null)
        continue;
      var truck = load.PlanningTruckId ?? stop.TruckId ?? load.TruckId;
      if (truck is not { } id || result.ContainsKey(id))
        continue;
      var trailer =
        Named(stop.TrailerId, stop.TrailerNumber)
        ?? Named(load.TrailerId, load.TrailerNumber);
      var number =
        stop.TrailerNumber.Length > 0 ? stop.TrailerNumber : load.TrailerNumber;
      if (trailer is null && TrailerUnits.Nameable(number) is not null)
        unknown.Add((load.Source ?? TruckTrailerSources.Load, number));
      fromLoads.Add((id, trailer));
    }
    foreach (var truck in fromLoads.GroupBy(x => x.Truck))
      result[truck.Key] = Answer(
        truck.Select(x => x.Trailer),
        TruckTrailerSources.Load
      );
    return new(result, unknown);
  }

  private static WorkTrailer Answer(IEnumerable<Guid?> named, string source)
  {
    var trailers = named.OfType<Guid>().Distinct().ToList();
    return trailers.Count switch
    {
      0 => new(null, source, false),
      1 => new(trailers[0], source, false),
      _ => new(null, source, true),
    };
  }
}
