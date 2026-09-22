using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Application.Features.Routing.Services.FuelPlanning;
using Domain.Models.Routing;
using Domain.Rules.Routing;
using Infrastructure.Integrations.GeoTimeZone;

// Splitting the corridor filter into its two halves.
//
// Production measured FuelRegionPlanner's "corridor" stage at 4,370.6 ms over
// 696 priced stations - 6.28 ms each - and that stage covers two things done
// per station:
//
//     var match = geometry.Match(x.Station.Point, progress, ct);
//     return match.Away <= 2 && countries.Matches(match.Point, x.Station);
//
// This runs each half separately over the same data, using the real
// FuelSearchGeometry and the real RouteRegionLookup rather than a model of
// them, so the answer is about the shipped algorithm.
//
//   dotnet run -c Release --project tools/FuelCorridorProbe -- <plan.json> <stations.json>
//
// <plan.json>     a copy of DispatchRoutePlans.PlanJson
// <stations.json> [{"lat":..,"lon":..,"country":".."}, ...] from FuelStations
//
// Both stay outside the repository. No database, no host, no provider.

if (args.Length < 2)
{
  Console.Error.WriteLine(
    "usage: dotnet run -c Release --project tools/FuelCorridorProbe -- <plan.json> <stations.json>"
  );
  return 64;
}

var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
var routeNode =
  JsonNode.Parse(File.ReadAllText(args[0])!)!.AsObject()["route"]
  ?? throw new InvalidOperationException("That plan has no route.");
var route =
  JsonSerializer.Deserialize<TruckRoute>(routeNode.ToJsonString(), options)
  ?? throw new InvalidOperationException("The route did not decode.");

var stations = JsonSerializer
  .Deserialize<List<StationRow>>(File.ReadAllText(args[1]), options)!
  .Where(x => x.Lat is not 0 && x.Lon is not 0)
  .Select(x => new FuelPlanStop
  {
    Point = new(x.Lat, x.Lon),
    Country = x.Country ?? "",
  })
  .ToList();

var points = route.Legs.Sum(x => x.Points.Count);
Console.WriteLine(
  $"runtime  : {Environment.Version}  "
    + $"{System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}"
);
Console.WriteLine(
  $"route    : {route.Legs.Count} legs, {points} points, {route.Miles:F0} mi"
);
Console.WriteLine($"stations : {stations.Count}");

var building = Stopwatch.StartNew();
var geometry = new FuelSearchGeometry(route);
building.Stop();
Console.WriteLine(
  $"index    : built in {building.Elapsed.TotalMilliseconds:F1} ms, "
    + $"{geometry.BlockCount} blocks, {geometry.Miles:F0} mi"
);
Console.WriteLine();

// --- half one: the road match ------------------------------------------------
var lookup = new RouteRegionLookup();
var matches = new (double Away, RoutePoint Point, int Segments)[stations.Count];

for (var warm = 0; warm < 2; warm++)
for (var i = 0; i < stations.Count; i++)
  geometry.Match(stations[i].Point);

var clock = Stopwatch.StartNew();
for (var i = 0; i < stations.Count; i++)
{
  var found = geometry.Match(stations[i].Point);
  matches[i] = (found.Away, found.Point, found.SegmentsExamined);
}
clock.Stop();
var matchMs = clock.Elapsed.TotalMilliseconds;

var segments = matches.Select(x => x.Segments).OrderBy(x => x).ToArray();
Console.WriteLine("geometry.Match over every station");
Console.WriteLine(
  $"  total {matchMs, 9:F1} ms   per station {matchMs / stations.Count, 7:F3} ms"
);
Console.WriteLine(
  $"  segments examined: min {segments[0]}, median {segments[segments.Length / 2]}, "
    + $"max {segments[^1]}, total {segments.Sum():N0}"
);
Console.WriteLine(
  $"  route has {points} points, so the median station examines "
    + $"{(double)segments[segments.Length / 2] / Math.Max(1, points) * 100:F0}% of them"
);
Console.WriteLine();

// --- half two: the country check ---------------------------------------------
// Fresh instance: FuelAccessCountries memoises per point, and a warmed cache
// would answer a question nobody asked.
for (var warm = 0; warm < 2; warm++)
{
  var warming = new FuelAccessCountries(lookup);
  for (var i = 0; i < stations.Count; i++)
    warming.Matches(matches[i].Point, stations[i]);
}

var countries = new FuelAccessCountries(lookup);
clock.Restart();
var kept = 0;
for (var i = 0; i < stations.Count; i++)
  if (countries.Matches(matches[i].Point, stations[i]))
    kept++;
clock.Stop();
var countryMs = clock.Elapsed.TotalMilliseconds;

Console.WriteLine("countries.Matches over every station (cold cache)");
Console.WriteLine(
  $"  total {countryMs, 9:F1} ms   per station {countryMs / stations.Count, 7:F3} ms"
);
Console.WriteLine($"  matched {kept}");
Console.WriteLine();

// --- both, in the order the filter runs them ---------------------------------
var both = new FuelAccessCountries(lookup);
clock.Restart();
var corridor = 0;
for (var i = 0; i < stations.Count; i++)
{
  var found = geometry.Match(stations[i].Point);
  if (found.Away <= 2 && both.Matches(found.Point, stations[i]))
    corridor++;
}
clock.Stop();

Console.WriteLine("the corridor filter as written");
Console.WriteLine(
  $"  total {clock.Elapsed.TotalMilliseconds, 9:F1} ms   "
    + $"per station {clock.Elapsed.TotalMilliseconds / stations.Count, 7:F3} ms"
);
Console.WriteLine($"  kept {corridor}");
Console.WriteLine();


// --- the corridor filter as it now stands ------------------------------------
// FuelSearchGeometry.Match takes maximumAwayMiles, and the corridor filter
// passes the same constant it judges by. This is that path, on any number of
// legs - no MatchLeg substitute and no single-leg restriction.
{
  var limit = FuelRegionPlanner.CorridorMiles;
  var bounding = new FuelAccessCountries(lookup);
  for (var warm = 0; warm < 2; warm++)
  for (var i = 0; i < stations.Count; i++)
    geometry.Match(stations[i].Point, 0, default, limit);

  clock.Restart();
  var within = 0;
  long bounded = 0;
  for (var i = 0; i < stations.Count; i++)
  {
    var found = geometry.Match(stations[i].Point, 0, default, limit);
    bounded += found.SegmentsExamined;
    if (found.Away <= limit && bounding.Matches(found.Point, stations[i]))
      within++;
  }
  clock.Stop();
  var boundedMs = clock.Elapsed.TotalMilliseconds;
  Console.WriteLine($"the corridor filter bounded at {limit} mi");
  Console.WriteLine(
    $"  total {boundedMs, 9:F1} ms   per station {boundedMs / stations.Count, 7:F3} ms"
  );
  Console.WriteLine(
    $"  segments examined {bounded:N0} against {segments.Sum():N0} unbounded"
  );
  Console.WriteLine($"  kept {within}, against {corridor} unbounded");
  if (within != corridor)
  {
    Console.Error.WriteLine("  the two forms disagree on the answer");
    return 70;
  }
  Console.WriteLine(
    $"  -> {(matchMs - boundedMs) / matchMs * 100:F0}% less time, "
      + $"{(1 - (double)bounded / segments.Sum()) * 100:F0}% fewer segments"
  );
  Console.WriteLine();
}

var share = matchMs / (matchMs + countryMs) * 100;
Console.WriteLine(
  $"Of the two halves, geometry.Match is {share:F0}% and the country check "
    + $"{100 - share:F0}%."
);
Console.WriteLine(
  "Short-circuiting means the filter as written skips the country check for"
);
Console.WriteLine(
  "stations further than two miles off the road, so it is not the sum of the"
);
Console.WriteLine("two halves measured separately.");
return 0;

internal sealed record StationRow(double Lat, double Lon, string? Country);
