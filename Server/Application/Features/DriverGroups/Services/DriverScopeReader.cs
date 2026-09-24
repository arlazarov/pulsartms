using Application.Caching;

namespace Application.Features.DriverGroups.Services;

// The one reading of the dispatcher's chosen group for a request, shared by
// every page that lists drivers' work, kept for the rest of the request.
//
// The choice and its members change only through DriverGroupHandlers,
// which drop the driver-groups read group after they commit, so they come
// from the read cache: no round trip for a dispatcher on All, whose pages
// poll every few seconds. The trucks a group's drivers are on change with
// the fleet and are read every time a group is chosen.
public sealed class DriverScopeReader(
  IAppDbContext db,
  ICurrentUser caller,
  ReadCache reads
) : IDriverScope
{
  private sealed record Choice(Guid Id, string Name, Guid[] Drivers);

  private static readonly string[] Live = ["planned", "active"];
  private Task<DriverScope>? _current;

  public Task<DriverScope> CurrentAsync(CancellationToken ct) =>
    _current ??= ReadAsync(ct);

  private async Task<DriverScope> ReadAsync(CancellationToken ct)
  {
    if (caller.IdentityUserId is not { Length: > 0 } identity)
      return DriverScope.All;
    if (
      await reads.GetAsync(
        ReadGroups.DriverGroups,
        identity,
        () => ChoiceAsync(identity, ct),
        ct: ct
      )
      is not { } choice
    )
      return DriverScope.All;
    var drivers = choice.Drivers;
    var trucks = await db
      .Trucks.AsNoTracking()
      .Where(x => x.DriverId != null && drivers.Contains(x.DriverId.Value))
      .Select(x => x.Id)
      .Union(
        db.ExecutionLegs.AsNoTracking()
          .Where(x =>
            Live.Contains(x.Status)
            && (
              x.DriverId != null && drivers.Contains(x.DriverId.Value)
              || x.CoDriverId != null && drivers.Contains(x.CoDriverId.Value)
            )
          )
          .Select(x => x.TruckId)
      )
      .ToListAsync(ct);
    return new DriverScope(choice.Id, choice.Name, drivers, trucks);
  }

  private async Task<Choice?> ChoiceAsync(string identity, CancellationToken ct)
  {
    var chosen = await db
      .Users.AsNoTracking()
      .Where(x =>
        x.IdentityUserId == identity
        && x.IsActive
        && x.SelectedDriverGroupId != null
      )
      .Select(x => new
      {
        Group = db
          .DriverGroups.Where(g =>
            g.Id == x.SelectedDriverGroupId && g.OwnerUserId == x.Id
          )
          .Select(g => new { g.Id, g.Name })
          .FirstOrDefault(),
        Drivers = db
          .DriverGroupMembers.Where(m => m.GroupId == x.SelectedDriverGroupId)
          .Select(m => m.DriverId)
          .ToList(),
      })
      .SingleOrDefaultAsync(ct);
    return chosen?.Group is { } group
      ? new(group.Id, group.Name, [.. chosen.Drivers])
      : null;
  }
}
