namespace Application.Features.DriverGroups.Services;

// The one reading of the dispatcher's chosen group for a request, shared by
// every page that lists drivers' work. Two reads when a group is chosen
// (the choice with its members, then the trucks they are on), one when it
// is not; the answer is kept for the rest of the request.
public sealed class DriverScopeReader(IAppDbContext db, ICurrentUser caller)
  : IDriverScope
{
  private static readonly string[] Live = ["planned", "active"];
  private Task<DriverScope>? _current;

  public Task<DriverScope> CurrentAsync(CancellationToken ct) =>
    _current ??= ReadAsync(ct);

  private async Task<DriverScope> ReadAsync(CancellationToken ct)
  {
    if (string.IsNullOrEmpty(caller.IdentityUserId))
      return DriverScope.All;
    var chosen = await db
      .Users.AsNoTracking()
      .Where(x =>
        x.IdentityUserId == caller.IdentityUserId
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
    if (chosen?.Group is not { } group)
      return DriverScope.All;
    var drivers = chosen.Drivers.ToArray();
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
    return new DriverScope(group.Id, group.Name, drivers, trucks);
  }
}
