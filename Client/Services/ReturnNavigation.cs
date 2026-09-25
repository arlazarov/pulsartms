namespace Client.Services;

// Where a load page returns to. A page that opens a load passes its own
// address, its state included, as `from`; the load page offers the way
// back to exactly that place. Only this app's own pages are accepted, so
// a link can never send the reader elsewhere; anything else, or nothing,
// returns to Dispatch. The rule is in docs/ui-controls.md, "Returning from
// a load".
public static class ReturnNavigation
{
  public const string Parameter = "from";

  public static readonly ReturnLink Fallback = new(
    "/dispatch",
    "Back to Dispatch"
  );

  private const int Longest = 2048;

  public static string Load(Guid id, string? origin, Guid? stopId = null)
  {
    var query = new List<string>();
    if (stopId is { } stop)
      query.Add($"stopId={stop}");
    // The plain list is where a load returns anyway; it is not carried.
    if (Accepts(origin) && origin != Fallback.Href)
      query.Add($"{Parameter}={Uri.EscapeDataString(origin!)}");
    return $"/dispatch/{id}"
      + (query.Count == 0 ? "" : "?" + string.Join('&', query));
  }

  public static ReturnLink Resolve(string? from) =>
    Accepts(from) ? new(from!, Label(Path(from!))) : Fallback;

  public static string FleetMap(Guid? truckId, Guid? dispatchId) =>
    Address(
      "/fleet/map",
      ("truckId", truckId?.ToString()),
      ("dispatchId", dispatchId?.ToString())
    );

  public static string DispatchList(
    Guid? truckId,
    bool completed,
    string search,
    int page
  ) =>
    Address(
      "/dispatch",
      ("truckId", truckId?.ToString()),
      ("scope", completed ? "completed" : null),
      ("q", search.Trim().Length > 0 ? search.Trim() : null),
      ("page", page > 1 ? page.ToString() : null)
    );

  public static string Conversation(Guid id) => $"/messages/{id}";

  // A path of this app with no scheme, host, fragment or escaping trick:
  // "//host" and "/\host" are other sites to a browser, so they, and
  // anything not one of the pages that open loads, are refused.
  private static bool Accepts(string? from)
  {
    if (
      string.IsNullOrEmpty(from)
      || from.Length > Longest
      || from[0] != '/'
      || from.Length > 1 && from[1] is '/' or '\\'
      || from.Any(x => x is '\\' or '#' || char.IsControl(x))
    )
      return false;
    var path = Path(from);
    return path is "/fleet/map" or "/dispatch" or "/messages"
      || path.StartsWith("/messages/", StringComparison.Ordinal)
        && Guid.TryParseExact(path["/messages/".Length..], "D", out _);
  }

  private static string Path(string from) =>
    from.IndexOf('?') is var query and >= 0 ? from[..query] : from;

  private static string Label(string path) =>
    path switch
    {
      "/fleet/map" => "Back to map",
      "/dispatch" => "Back to Dispatch",
      "/messages" => "Back to Messages",
      _ => "Back to conversation",
    };

  private static string Address(
    string path,
    params (string Name, string? Value)[] query
  )
  {
    var parts = query
      .Where(x => x.Value is not null)
      .Select(x => $"{x.Name}={Uri.EscapeDataString(x.Value!)}")
      .ToList();
    return parts.Count == 0 ? path : $"{path}?{string.Join('&', parts)}";
  }
}

public sealed record ReturnLink(string Href, string Label);
