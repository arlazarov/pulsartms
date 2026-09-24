namespace Application.Interfaces;

// What the signed-in dispatcher's chosen driver group narrows the
// program's driver lists to: Messages, Dispatch, loads and Fleet Map read
// the same answer, so one choice holds everywhere. Owned by DriverGroups.
// It only narrows what the caller may already see and never widens it;
// without a signed-in user (a background job) or with no group chosen, it
// is All. Read once per request.
//
// It narrows only a page's own list, asked for by the query's
// InChosenGroup. A read another owner makes inside the request (a truck's
// position for its planning, the fleet preview kept for everyone) is
// unscoped: a personal filter must never shape shared state or a shared
// cache.
public interface IDriverScope
{
  Task<DriverScope> CurrentAsync(CancellationToken ct);
}

// Drivers: the group's drivers. Trucks: the trucks they are on now - the
// truck each is assigned to in the fleet, and the trucks of their planned
// and active execution legs as driver or co-driver.
public sealed class DriverScope
{
  private readonly HashSet<Guid> _drivers;
  private readonly HashSet<Guid> _trucks;

  public static readonly DriverScope All = new(null, null, [], []);

  public DriverScope(
    Guid? groupId,
    string? groupName,
    IReadOnlyCollection<Guid> drivers,
    IReadOnlyCollection<Guid> trucks
  )
  {
    GroupId = groupId;
    GroupName = groupName;
    _drivers = [.. drivers];
    _trucks = [.. trucks];
    Drivers = [.. _drivers];
    Trucks = [.. _trucks];
  }

  public Guid? GroupId { get; }
  public string? GroupName { get; }
  public bool IsAll => GroupId is null;

  // As arrays, for a database query's Contains.
  public Guid[] Drivers { get; }
  public Guid[] Trucks { get; }

  public bool IncludesDriver(Guid? driver) =>
    IsAll || driver is { } id && _drivers.Contains(id);

  public bool IncludesTruck(Guid? truck) =>
    IsAll || truck is { } id && _trucks.Contains(id);
}
