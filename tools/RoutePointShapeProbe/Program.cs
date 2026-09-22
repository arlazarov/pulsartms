using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using RoutePointShapeProbe;

// Isolated on purpose: no host, no background services, no database, no
// provider. It reads saved plan copies from a directory given on the command
// line. Those copies stay outside the repository.

const int Rounds = 7;
const int Iterations = 20;
const int Stations = 24; // FuelRegionOptions.CandidateShortlistLimit
const int Reads = 200;

var directory = args.FirstOrDefault();
if (directory is null)
{
  Console.Error.WriteLine(
    "usage: dotnet run -c Release --project tools/RoutePointShapeProbe -- <plans-directory>"
  );
  Console.Error.WriteLine(
    "  <plans-directory> holds *.json copies of DispatchRoutePlans.PlanJson."
  );
  Console.Error.WriteLine("  Keep them outside the repository.");
  return 64;
}
if (!Directory.Exists(directory))
{
  Console.Error.WriteLine($"no such directory: {directory}");
  return 66;
}
var files = Directory.GetFiles(directory, "*.json").OrderBy(x => x).ToArray();
if (files.Length == 0)
{
  Console.Error.WriteLine($"no plan copies in {Path.GetFullPath(directory)}");
  return 66;
}

Console.WriteLine(
  $"runtime   : {Environment.Version}  {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}"
);
Console.WriteLine($"server gc : {System.Runtime.GCSettings.IsServerGC}");
Console.WriteLine($"plans     : {files.Length} copies");
Console.WriteLine(
  $"rounds    : {Rounds} x {Iterations} iterations, alternating, median"
);
Console.WriteLine();

foreach (var file in files)
{
  var name = Path.GetFileNameWithoutExtension(file);
  var route = JsonNode.Parse(File.ReadAllText(file))!.AsObject()["route"]!;
  var fullJson = JsonSerializer.SerializeToUtf8Bytes(route, ProbeJson.Options);
  var full = JsonSerializer.Deserialize<TruckRouteClass>(
    fullJson,
    ProbeJson.Options
  )!;
  var fullPoints = full.Legs.Sum(x => x.Points.Count);

  // displayJson: TrimForDisplay simplifies each leg at two metres.
  var display = Rebuild(full, leg => DisplaySimplification.Run(leg.Points));
  var displayJson = JsonSerializer.SerializeToUtf8Bytes(
    display,
    ProbeJson.Options
  );
  var displayPoints = display.Legs.Sum(x => x.Points.Count);

  // metadataJson: GeometryOmitted, so every leg loses its points.
  var metadataJson = JsonSerializer.SerializeToUtf8Bytes(
    Rebuild(full, _ => []),
    ProbeJson.Options
  );

  Console.WriteLine(
    $"=== {name[..8]}  legs={full.Legs.Count}  "
      + $"full={fullPoints} pts ({fullJson.Length / 1024} KiB)  "
      + $"display={displayPoints} pts ({displayJson.Length / 1024} KiB)  "
      + $"metadata={metadataJson.Length} B ==="
  );

  if (!Agree(fullJson))
  {
    Console.Error.WriteLine("  the two shapes decoded different values");
    return 70;
  }
  Console.WriteLine("  both shapes decode identical coordinates: yes");

  Decoding("A cold load, full geometry", fullJson, fullPoints);
  Decoding("B display, simplified     ", displayJson, displayPoints);
  Decoding("C metadata only           ", metadataJson, 0);
  Boundary("D index boundary  [MODEL] ", full);
  Console.WriteLine();
}

return 0;

static TruckRouteClass Rebuild(
  TruckRouteClass source,
  Func<RouteLegClass, List<RoutePointClass>> points
) =>
  new()
  {
    CalculatedAt = source.CalculatedAt,
    Miles = source.Miles,
    Seconds = source.Seconds,
    Warnings = source.Warnings,
    Legs = source
      .Legs.Select(leg => leg with { Points = points(leg) })
      .ToList(),
  };

static bool Agree(byte[] json)
{
  var asClasses = JsonSerializer.Deserialize<TruckRouteClass>(
    json,
    ProbeJson.Options
  )!;
  var asValues = JsonSerializer.Deserialize<TruckRouteValue>(
    json,
    ProbeJson.Options
  )!;
  return asClasses.Legs.Count == asValues.Legs.Count
    && asClasses
      .Legs.Zip(asValues.Legs)
      .All(pair =>
        pair.First.Points.Count == pair.Second.Points.Count
        && pair.First.Points.Zip(pair.Second.Points)
          .All(point =>
            point.First.Latitude == point.Second.Latitude
            && point.First.Longitude == point.Second.Longitude
          )
      );
}

void Decoding(string label, byte[] json, int points)
{
  var (asClass, asValue) = Pair(
    () =>
      JsonSerializer
        .Deserialize<TruckRouteClass>(json, ProbeJson.Options)!
        .Legs.Count,
    () =>
      JsonSerializer
        .Deserialize<TruckRouteValue>(json, ProbeJson.Options)!
        .Legs.Count
  );
  Report($"{label} ({points} pts)", asClass, asValue);
}

// The access pattern the fuel search uses: match the shortlisted stations onto
// the road, then read arrival points back along it. See IndexBoundaryModel for
// what this does and does not represent.
void Boundary(string label, TruckRouteClass route)
{
  var points = route.Legs.SelectMany(x => x.Points).ToList();
  var asClasses = new BoundaryReturningClass(points);
  var asValues = new BoundaryReturningValue(points);
  var stations = Enumerable
    .Range(0, Stations)
    .Select(i => points[(int)((long)i * (points.Count - 1) / Stations)])
    .ToArray();
  var miles = asClasses.Miles;
  var (asClass, asValue) = Pair(
    () =>
    {
      var total = 0d;
      foreach (var station in stations)
        total += asClasses.Match(station).Along;
      for (var i = 0; i < Reads; i++)
        total += asClasses.At(miles * i / Reads).Latitude;
      return (int)total;
    },
    () =>
    {
      var total = 0d;
      foreach (var station in stations)
        total += asValues.Match(new(station.Latitude, station.Longitude)).Along;
      for (var i = 0; i < Reads; i++)
        total += asValues.At(miles * i / Reads).Latitude;
      return (int)total;
    }
  );
  Report($"{label} ({Stations} Match + {Reads} At)", asClass, asValue);
}

// Both shapes are warmed well past the tiering threshold and then measured in
// ALTERNATING rounds. Measuring one shape to completion and then the other
// makes whichever ran first pay for tier-0 code; that mistake produced a 20%
// reading in the wrong direction before it was caught.
(Sample AsClass, Sample AsValue) Pair(Func<int> asClass, Func<int> asValue)
{
  for (var i = 0; i < 300; i++)
  {
    asClass();
    asValue();
  }
  Thread.Sleep(50); // let background tier-1 compilation land
  for (var i = 0; i < 50; i++)
  {
    asClass();
    asValue();
  }
  var classSamples = new Samples();
  var valueSamples = new Samples();
  for (var round = 0; round < Rounds; round++)
    if (round % 2 == 0)
    {
      classSamples.Add(Measure(asClass));
      valueSamples.Add(Measure(asValue));
    }
    else
    {
      valueSamples.Add(Measure(asValue));
      classSamples.Add(Measure(asClass));
    }
  return (classSamples.Median(), valueSamples.Median());
}

Sample Measure(Func<int> action)
{
  GC.Collect(2, GCCollectionMode.Forced, blocking: true);
  GC.WaitForPendingFinalizers();
  GC.Collect(2, GCCollectionMode.Forced, blocking: true);
  var allocated = GC.GetTotalAllocatedBytes(precise: true);
  var gen0 = GC.CollectionCount(0);
  var gen1 = GC.CollectionCount(1);
  var gen2 = GC.CollectionCount(2);
  var clock = Stopwatch.StartNew();
  for (var i = 0; i < Iterations; i++)
    action();
  clock.Stop();
  return new(
    (GC.GetTotalAllocatedBytes(precise: true) - allocated) / (double)Iterations,
    clock.Elapsed.TotalMilliseconds / Iterations,
    (GC.CollectionCount(0) - gen0) / (double)Iterations,
    (GC.CollectionCount(1) - gen1) / (double)Iterations,
    (GC.CollectionCount(2) - gen2) / (double)Iterations
  );
}

static void Report(string label, Sample asClass, Sample asValue)
{
  static string Change(double from, double to) =>
    from <= 0 ? "     -" : $"{(to - from) / from * 100, +5:F0}%";
  Console.WriteLine($"  {label}");
  Console.WriteLine(
    $"      bytes/op   class {asClass.Bytes, 12:N0}   value {asValue.Bytes, 12:N0}   {Change(asClass.Bytes, asValue.Bytes)}"
  );
  Console.WriteLine(
    $"      ms/op      class {asClass.Milliseconds, 12:F3}   value {asValue.Milliseconds, 12:F3}   {Change(asClass.Milliseconds, asValue.Milliseconds)}"
  );
  Console.WriteLine(
    $"      collections gen0 {asClass.Gen0:F2}/{asValue.Gen0:F2}  gen1 {asClass.Gen1:F2}/{asValue.Gen1:F2}  gen2 {asClass.Gen2:F2}/{asValue.Gen2:F2}  (too few to read; see README)"
  );
}
