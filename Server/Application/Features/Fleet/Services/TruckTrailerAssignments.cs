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
  public static Task<IReadOnlyCollection<Guid>> ResolveAsync(
    IAppDbContext db,
    CancellationToken ct
  ) => ResolveCoreAsync(db, null, ct);

  // The same for the trucks a committed change touched, and only the trucks
  // their trailers tie them to: the ones holding, disputing or reported
  // with one of those trailers. The rest of the fleet is not read.
  public static Task<IReadOnlyCollection<Guid>> ResolveTrucksAsync(
    IAppDbContext db,
    IReadOnlyCollection<Guid> trucks,
    CancellationToken ct
  ) =>
    trucks.Count == 0
      ? Task.FromResult<IReadOnlyCollection<Guid>>([])
      : ResolveCoreAsync(db, trucks, ct);

  private static async Task<IReadOnlyCollection<Guid>> ResolveCoreAsync(
    IAppDbContext db,
    IReadOnlyCollection<Guid>? requested,
    CancellationToken ct
  )
  {
    var reading = await DiscoverAsync(db, requested, ct);
    var work = reading.Trucks;
    var scope = requested is null
      ? await db.Trucks.ToListAsync(ct)
      : await db.Trucks.Where(x => requested.Contains(x.Id)).ToListAsync(ct);
    if (requested is not null)
    {
      var tied = Trailers(scope, work);
      var scoped = scope.Select(x => x.Id).ToArray();
      var related = await db
        .Trucks.Where(x =>
          !scoped.Contains(x.Id)
          && (
            x.TrailerId.HasValue && tied.Contains(x.TrailerId.Value)
            || x.TrailerConflictId.HasValue
              && tied.Contains(x.TrailerConflictId.Value)
            || x.TelemetryTrailerId.HasValue
              && tied.Contains(x.TelemetryTrailerId.Value)
          )
        )
        .ToListAsync(ct);
      if (related.Count > 0)
      {
        var more = await DiscoverAsync(
          db,
          related.Select(x => x.Id).ToArray(),
          ct
        );
        foreach (var (truck, trailer) in more.Trucks)
          work.TryAdd(truck, trailer);
        scope.AddRange(related);
      }
    }
    var candidates = Trailers(scope, work);
    var usable = (
      await db
        .Trailers.AsNoTracking()
        .Where(x => x.IsActive && candidates.Contains(x.Id))
        .Select(x => x.Id)
        .ToListAsync(ct)
    ).ToHashSet();
    var proposed = scope.ToDictionary(
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
    );
    var trucks = scope.ToDictionary(x => x.Id);
    if (requested is not null)
    {
      var inScope = trucks.Keys.ToArray();
      // A truck outside the scope that holds a trailer the scope now
      // claims is weighed as it stands, so one trailer is never on two.
      var claimed = proposed
        .Values.Select(x => x.TrailerId)
        .OfType<Guid>()
        .ToArray();
      foreach (
        var holder in await db
          .Trucks.Where(x =>
            !inScope.Contains(x.Id)
            && x.TrailerId.HasValue
            && claimed.Contains(x.TrailerId.Value)
          )
          .ToListAsync(ct)
      )
      {
        trucks[holder.Id] = holder;
        proposed[holder.Id] = new(
          holder.TrailerId,
          holder.TrailerSource,
          holder.TrailerConflictId
        );
      }
    }
    var resolved = TruckTrailerAuthority.Settle(proposed);
    var changed = trucks
      .Values.Where(x =>
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

  // Reads the work, cataloguing first any trailer a current load names that
  // no source has reported, through the catalog's owner.
  private static async Task<TruckWorkTrailers.Reading> DiscoverAsync(
    IAppDbContext db,
    IReadOnlyCollection<Guid>? trucks,
    CancellationToken ct
  )
  {
    var reading = await TruckWorkTrailers.ReadAsync(db, trucks, ct);
    if (reading.Unknown.Count == 0)
      return reading;
    foreach (var source in reading.Unknown.GroupBy(x => x.Source))
      await TrailerCatalog.EnsureAsync(
        db,
        source.Key,
        source.Select(x => x.Number),
        ct
      );
    await db.SaveChangesAsync(ct);
    return await TruckWorkTrailers.ReadAsync(db, trucks, ct);
  }

  private static HashSet<Guid> Trailers(
    IEnumerable<Truck> trucks,
    IReadOnlyDictionary<Guid, WorkTrailer> work
  ) =>
    trucks
      .SelectMany(x =>
        new[] { x.TrailerId, x.TrailerConflictId, x.TelemetryTrailerId }
      )
      .Concat(work.Values.Select(x => x.TrailerId))
      .OfType<Guid>()
      .ToHashSet();

  // After a dispatcher's committed change to current work: the trucks it
  // touched are resolved at once, not at the next synchronization. It waits
  // briefly for a running synchronization; if that does not finish, the
  // synchronization's own pass picks the change up.
  public static async Task<bool> RefreshTrucksAsync(
    IAppDbContext db,
    ReadCache reads,
    IReadOnlyCollection<Guid> trucks,
    CancellationToken ct
  )
  {
    if (trucks.Count == 0)
      return false;
    if (!await ProcessGates.Fleet.WaitAsync(TimeSpan.FromSeconds(5), ct))
      return false;
    try
    {
      await using var transaction = await db.Database.BeginTransactionAsync(ct);
      var changed = await ResolveTrucksAsync(db, trucks, ct);
      if (changed.Count == 0)
        return false;
      await db.SaveChangesAsync(ct);
      await transaction.CommitAsync(ct);
      Published(reads);
      return true;
    }
    finally
    {
      ProcessGates.Fleet.Release();
    }
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
        // The whole fleet is resolved here; the trucks this request marked
        // need no second pass.
        TruckWorkChanges.Handled();
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
