using System.Globalization;

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
    FleetMap(new MapPlace(truckId, dispatchId));

  public static string FleetMap(MapPlace place) =>
    Address(
      "/fleet/map",
      ("truckId", place.TruckId?.ToString()),
      ("dispatchId", place.DispatchId?.ToString()),
      ("nextLoadId", place.NextLoadId?.ToString()),
      ("nextStop", place.NextLoadId is null ? null : place.NextStop.ToString()),
      ("nextLeg", place.NextLoadId is null ? null : place.NextLeg?.ToString()),
      ("q", place.Search?.Trim().Length > 0 ? place.Search.Trim() : null)
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

// Where the map was: the chosen truck and load, a next load's stop being
// looked at and the truck search. The camera is not part of an address:
// the map keeps it in the tab (FleetMap.ReturnPlace).
public sealed record MapPlace(
  Guid? TruckId,
  Guid? DispatchId,
  Guid? NextLoadId = null,
  int NextStop = 0,
  Guid? NextLeg = null,
  string? Search = null
);

// The camera, written compactly and read back only when it is a real
// place on the map. An older link may still carry it as `view`.
public sealed record MapView(double Latitude, double Longitude, double Zoom)
{
  public override string ToString() =>
    string.Create(
      CultureInfo.InvariantCulture,
      $"{Latitude:0.#####},{Longitude:0.#####},{Zoom:0.##}"
    );

  public static MapView? Parse(string? text)
  {
    var parts = text?.Split(',');
    if (
      parts is not { Length: 3 }
      || !double.TryParse(parts[0], Number, Invariant, out var latitude)
      || !double.TryParse(parts[1], Number, Invariant, out var longitude)
      || !double.TryParse(parts[2], Number, Invariant, out var zoom)
      || !double.IsFinite(latitude + longitude + zoom)
      || latitude is < -85 or > 85
      || longitude is < -180 or > 180
      || zoom is < 1 or > 22
    )
      return null;
    return new(latitude, longitude, zoom);
  }

  private const NumberStyles Number = NumberStyles.Float;
  private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
}
