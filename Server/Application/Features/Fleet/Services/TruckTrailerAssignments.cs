using Application.Caching;
using Application.Concurrency;
using Domain.Entities.Fleet;
using Domain.Models.Fleet;
using Domain.Rules.Fleet;

namespace Application.Features.Fleet.Services;

// The one owner of which trailer each truck has now. It is separate from
// the catalog: a trailer existing says nothing about where it is.
//
// Two kinds of evidence are kept apart and weighed by TruckTrailerAuthority.
// The telemetry provider's word is stored per truck and replaced only when
// the provider answers; an answer without a current assignment is "not
// known", never a detach. The current work is read fresh each time: the
// truck's active execution leg if it has one, otherwise its in-transit
// imported load at the stop it is working. Planned, future and finished
// loads say nothing about the trailer on the truck now.
public static class TruckTrailerAssignments
{
  public static IReadOnlyDictionary<Guid, TelemetryTrailer> Telemetry(
    IReadOnlyDictionary<Guid, string> truckDrivers,
    IReadOnlyList<ExternalTrailerAssignment> assignments,
    IReadOnlyDictionary<string, Guid> trailers,
    DateTime now
  )
  {
    var byDriver = assignments
      .Where(x => !string.IsNullOrWhiteSpace(x.DriverExternalId))
      .ToLookup(x => x.DriverExternalId, StringComparer.OrdinalIgnoreCase);
    var facts = new Dictionary<Guid, TelemetryTrailer>();
    foreach (var (truck, driver) in truckDrivers)
    {
      var records = byDriver[driver].ToList();
      var current = records
        .Where(x =>
          x.StartTime <= now
          && (x.EndTime is null || x.EndTime > now)
          && !string.IsNullOrWhiteSpace(x.TrailerExternalId)
        )
        .Select(x => x.TrailerExternalId)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
      facts[truck] = current.Count switch
      {
        1 when trailers.TryGetValue(current[0], out var id) => new(true, id),
        0 when records.Count > 0 && records.All(x => x.EndTime <= now) => new(
          true,
          null
        ),
        _ => TelemetryTrailer.Unknown,
      };
    }
    return facts;
  }

  // Stores what the provider said. Trucks it said nothing certain about
  // lose their earlier certainty: the provider no longer reports it.
  public static void Record(
    IEnumerable<Truck> trucks,
    IReadOnlyDictionary<Guid, TelemetryTrailer> facts
  )
  {
    foreach (var truck in trucks)
    {
      var fact = facts.GetValueOrDefault(truck.Id, TelemetryTrailer.Unknown);
      truck.TelemetryTrailerKnown = fact.Known;
      truck.TelemetryTrailerId = fact.TrailerId;
    }
  }

  // Refreshes every truck's trailer from its stored telemetry and current
  // work, inside the caller's transaction, and returns the trucks whose
  // trailer or its standing changed - the one that lost a trailer as well
  // as the one that took it.
  public static async Task<IReadOnlyCollection<Guid>> ResolveAsync(
    IAppDbContext db,
    CancellationToken ct
  )
  {
    var trucks = await db.Trucks.ToListAsync(ct);
    var reading = await TruckWorkTrailers.ReadAsync(db, ct);
    if (reading.Unknown.Count > 0)
    {
      // A load names a trailer no source has reported: it is catalogued
      // through the catalog's owner, then the work is read again.
      foreach (var source in reading.Unknown.GroupBy(x => x.Source))
        await TrailerCatalog.EnsureAsync(
          db,
          source.Key,
          source.Select(x => x.Number),
          ct
        );
      await db.SaveChangesAsync(ct);
      reading = await TruckWorkTrailers.ReadAsync(db, ct);
    }
    var work = reading.Trucks;
    var active = await db
      .Trailers.AsNoTracking()
      .Where(x => x.IsActive)
      .Select(x => x.Id)
      .ToListAsync(ct);
    var usable = active.ToHashSet();
    var resolved = TruckTrailerAuthority.Settle(
      trucks.ToDictionary(
        x => x.Id,
        x =>
        {
          if (!x.IsActive)
            return new EffectiveTrailer(null, null, null);
          var trailer = TruckTrailerAuthority.Resolve(
            new(x.TelemetryTrailerKnown, x.TelemetryTrailerId),
            work.GetValueOrDefault(x.Id, WorkTrailer.None)
          );
          // An inactive trailer is unavailable, whoever names it.
          return trailer.TrailerId is { } id && !usable.Contains(id)
            ? new(null, null, id)
            : trailer;
        }
      )
    );
    var changed = trucks
      .Where(x =>
        x.TrailerId != resolved[x.Id].TrailerId
        || x.TrailerSource != resolved[x.Id].Source
        || x.TrailerConflictId != resolved[x.Id].ConflictId
      )
      .ToList();
    if (changed.Count == 0)
      return [];
    // A trailer is on one truck: every release is written before any new
    // holder is, so a swap never meets the one-truck constraint half done.
    foreach (var truck in changed.Where(x => x.TrailerId.HasValue))
    {
      truck.Trailer = null;
      truck.TrailerId = null;
    }
    await db.SaveChangesAsync(ct);
    foreach (var truck in changed)
    {
      var trailer = resolved[truck.Id];
      truck.TrailerId = trailer.TrailerId;
      truck.TrailerSource = trailer.Source;
      truck.TrailerConflictId = trailer.ConflictId;
    }
    return changed.Select(x => x.Id).ToArray();
  }

  // After an import or an execution change: refreshes and publishes on its
  // own, unless the fleet synchronization is running - it refreshes every
  // cycle itself, so waiting behind it would only hold the caller up.
  public static async Task RefreshAsync(
    IAppDbContext db,
    ReadCache reads,
    CancellationToken ct
  )
  {
    if (!await ProcessGates.Fleet.WaitAsync(0, ct))
      return;
    try
    {
      IReadOnlyCollection<Guid> changed;
      await using (
        var transaction = await db.Database.BeginTransactionAsync(ct)
      )
      {
        changed = await ResolveAsync(db, ct);
        if (changed.Count == 0)
          return;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
      }
      Published(reads);
    }
    finally
    {
      ProcessGates.Fleet.Release();
    }
  }

  // After the commit: the map's fleet metadata and the board both read the
  // truck's trailer.
  public static void Published(ReadCache reads)
  {
    reads.Invalidate("fleet-catalog");
    reads.Invalidate("board");
  }
}
