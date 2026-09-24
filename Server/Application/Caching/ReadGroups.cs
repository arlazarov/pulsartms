namespace Application.Caching;

// The read-cache groups by name, so a misspelt group is a compile error
// rather than an invalidation that silently misses. The names are also
// what other instances receive (CacheInvalidation rows) and must not
// change.
public static class ReadGroups
{
  public const string Dispatch = "dispatch";
  public const string Board = "board";
  public const string Execution = "execution";
  public const string RoutePreviews = "route-previews";
  public const string Fuel = "fuel";
  public const string FleetCatalog = "fleet-catalog";
  public const string Settings = "settings";

  // What a change to a load's work - its stops, their times or order, its
  // truck or its execution - makes stale.
  public static readonly IReadOnlyList<string> Work =
  [
    Dispatch,
    Board,
    Execution,
    RoutePreviews,
  ];
}
