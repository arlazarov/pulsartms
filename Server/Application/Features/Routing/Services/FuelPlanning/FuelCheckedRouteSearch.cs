using Application.Features.Routing.Interfaces;
using Domain.Models.Routing;
using Domain.Rules;
using Domain.Rules.Routing;

namespace Application.Features.Routing.Services.FuelPlanning;

public sealed record FuelCheckedRouteResult(
  TruckRoute? Route,
  List<FuelCandidate> Stations,
  string? RejectionReason = null,
  bool BudgetExceeded = false
);

// One sequential search owns this cache; recipes never cross a mandatory stop
// or request.
public sealed partial class FuelCheckedRouteSearch
{
  private sealed record Visit(Guid StationId, RoutePoint Point);

  private sealed record Recipe(
    Visit[] Visits,
    RouteLeg Leg,
    double[] FuelMiles,
    DateTime CalculatedAt,
    IReadOnlyList<string> Warnings,
    long Used
  );

  private readonly TruckRoute baseline;
  private readonly IReadOnlyList<PlanStop> stops;
  private readonly TruckRouteProfile profile;
  private readonly IRoutingProvider routing;
  private readonly int maximumRoadChecks;
  private readonly int maximumGeometryPoints;
  private readonly Recipe?[] recipes;
  private readonly double[] boundaries;
  private long sequence;
  public int RoadChecks { get; private set; } = 1;
  public int RetainedPoints { get; private set; }

  public FuelCheckedRouteSearch(
    TruckRoute baseline,
    IReadOnlyList<PlanStop> stops,
    TruckRouteProfile profile,
    IRoutingProvider routing,
    int maximumRoadChecks = 12,
    int maximumGeometryPoints = 200_000
  )
  {
    if (maximumRoadChecks < 1)
      throw new ArgumentOutOfRangeException(nameof(maximumRoadChecks));
    if (maximumGeometryPoints is < 2 or > 200_000)
      throw new ArgumentOutOfRangeException(nameof(maximumGeometryPoints));
    this.maximumGeometryPoints = maximumGeometryPoints;
    if (
      stops.Count is < 1 or > 40
      || stops.Any(stop => stop?.Point?.IsValid != true)
      || baseline.Miles <= 0
      || !ValidGeometry(baseline, stops.Count, out _, out var seconds)
      || !RouteAnchoring.Matches(
        baseline,
        new[] { baseline.Legs[0].Points[0] }
          .Concat(stops.Select(stop => stop.Point))
          .ToArray()
      )
    )
      throw new RoutePlanningException(
        "The fuel baseline geometry is incomplete or does not reach its mandatory stops."
      );
    this.baseline = new()
    {
      Legs = baseline.Legs,
      Miles = baseline.Legs.Sum(leg => leg.Miles),
      Seconds = seconds,
      Warnings = baseline.Warnings,
      CalculatedAt = baseline.CalculatedAt,
    };
    this.stops = stops;
    this.profile = profile;
    this.routing = routing;
    this.maximumRoadChecks = maximumRoadChecks;
    recipes = new Recipe?[stops.Count];
    double miles = 0;
    boundaries = baseline.Legs.Select(leg => miles += leg.Miles).ToArray();
  }

  public async Task<FuelCheckedRouteResult> CheckAsync(
    IReadOnlyList<FuelCandidate> chain,
    CancellationToken ct
  )
  {
    ct.ThrowIfCancellationRequested();
    if (chain.Count + stops.Count + 1 > 50)
      return Reject("The complete candidate exceeds the 50-waypoint limit.");
    var grouped = Enumerable
      .Range(0, stops.Count)
      .Select(_ => new List<FuelCandidate>())
      .ToArray();
    var visits = new HashSet<string>(StringComparer.Ordinal);
    foreach (var candidate in chain)
    {
      if (
        candidate?.Station
          is not { StationId: var stationId, Point.IsValid: true }
        || stationId == Guid.Empty
        || candidate.LegIndex < 0
        || candidate.LegIndex >= stops.Count
        || !double.IsFinite(candidate.AlongMiles)
        || candidate.AlongMiles
          < (candidate.LegIndex == 0 ? 0 : boundaries[candidate.LegIndex - 1])
            - .01
        || candidate.AlongMiles > boundaries[candidate.LegIndex] + .01
        || !visits.Add(candidate.VisitKey)
      )
        return Reject(
          "The candidate fuel visits do not match the mandatory route legs."
        );
      grouped[candidate.LegIndex].Add(candidate);
    }
    for (var index = 0; index < grouped.Length; index++)
      grouped[index] = grouped[index].OrderBy(fuel => fuel.AlongMiles).ToList();
    var keys = grouped
      .Select(group =>
        group
          .Select(fuel => new Visit(fuel.Station.StationId, fuel.Station.Point))
          .ToArray()
      )
      .ToArray();
    var needed = Enumerable
      .Range(0, stops.Count)
      .Where(index =>
        grouped[index].Count > 0
        && (
          recipes[index] is not { } saved
          || !saved.Visits.SequenceEqual(keys[index])
        )
      )
      .ToArray();
    if (needed.Length > maximumRoadChecks - RoadChecks)
      return RoadChecks < maximumRoadChecks
        ? await CheckWholeAsync(grouped, ct)
        : new(
          null,
          [],
          "The remaining road-check budget cannot verify this complete candidate.",
          BudgetExceeded: true
        );
    foreach (var index in needed)
      Remove(index);
    long pointCount = Enumerable
      .Range(0, stops.Count)
      .Where(index => grouped[index].Count == 0)
      .Sum(index => (long)baseline.Legs[index].Points.Count);
    pointCount += Enumerable
      .Range(0, stops.Count)
      .Where(index => grouped[index].Count > 0 && recipes[index] is not null)
      .Sum(index => (long)recipes[index]!.Leg.Points.Count);
    if (pointCount > maximumGeometryPoints)
      return Reject(
        "The complete checked candidate exceeds the geometry limit."
      );
    foreach (var index in needed)
    {
      var leg = baseline.Legs[index];
      var requested = new[] { leg.Points[0] }
        .Concat(grouped[index].Select(fuel => fuel.Station.Point))
        .Append(leg.Points[^1])
        .ToArray();
      ct.ThrowIfCancellationRequested();
      RoadChecks++;
      var raw = await routing.CalculateAsync(requested, profile, ct);
      ct.ThrowIfCancellationRequested();
      if (
        !ValidGeometry(
          raw,
          requested.Length - 1,
          out var points,
          out var seconds
        )
        || !RouteAnchoring.Matches(raw, requested)
        || !RouteAnchoring.Near(
          raw.Legs[0].Points[0],
          leg.Points[0],
          RouteAnchoring.ContinuityToleranceMiles
        )
        || !RouteAnchoring.Near(
          raw.Legs[^1].Points[^1],
          leg.Points[^1],
          RouteAnchoring.ContinuityToleranceMiles
        )
        || !RouteAnchoring.Near(
          raw.Legs[^1].Points[^1],
          stops[index].Point,
          RouteAnchoring.FacilityToleranceMiles
        )
      )
        return Reject(
          "The checked fuel segment has invalid geometry, timing or stop anchoring."
        );
      var collapsedCount = points - (raw.Legs.Count - 1);
      if (pointCount + collapsedCount > maximumGeometryPoints)
        return Reject(
          "The complete checked candidate exceeds the geometry limit."
        );
      var path = new List<RoutePoint>(collapsedCount);
      var fuelMiles = new double[grouped[index].Count];
      double miles = 0;
      for (var part = 0; part < raw.Legs.Count; part++)
      {
        var current = raw.Legs[part];
        path.AddRange(part == 0 ? current.Points : current.Points.Skip(1));
        miles += current.Miles;
        if (part < fuelMiles.Length)
          fuelMiles[part] = miles;
      }
      var recipe = new Recipe(
        keys[index],
        new(miles, seconds, path),
        fuelMiles,
        raw.CalculatedAt,
        raw.Warnings.ToArray(),
        ++sequence
      );
      while (RetainedPoints + collapsedCount > maximumGeometryPoints)
      {
        var oldest = Enumerable
          .Range(0, recipes.Length)
          .Where(other =>
            grouped[other].Count == 0 && recipes[other] is not null
          )
          .OrderBy(other => recipes[other]!.Used)
          .FirstOrDefault(-1);
        if (oldest < 0)
          return Reject(
            "The checked segment cache exceeds the geometry limit."
          );
        Remove(oldest);
      }
      recipes[index] = recipe;
      RetainedPoints += collapsedCount;
      pointCount += collapsedCount;
    }
    var result = new TruckRoute
    {
      CalculatedAt = baseline.CalculatedAt,
      Warnings = baseline.Warnings.ToList(),
    };
    var stations = new List<FuelCandidate>(chain.Count);
    for (var index = 0; index < stops.Count; index++)
    {
      var recipe = grouped[index].Count > 0 ? recipes[index] : null;
      var leg = recipe?.Leg ?? baseline.Legs[index];
      if (
        index > 0
        && !RouteAnchoring.Near(
          result.Legs[^1].Points[^1],
          leg.Points[0],
          RouteAnchoring.ContinuityToleranceMiles
        )
      )
        return Reject(
          "The checked fuel segments do not form a continuous route."
        );
      if (recipe is not null)
      {
        recipes[index] = recipe with { Used = ++sequence };
        for (var visit = 0; visit < grouped[index].Count; visit++)
        {
          var fuel = grouped[index][visit];
          stations.Add(
            fuel with
            {
              Station = FuelRouteVariant.CheckedStation(fuel.Station),
              AlongMiles = result.Miles + recipe.FuelMiles[visit],
              ExtraInMiles = 0,
              ExtraOutMiles = 0,
              EntryMiles = null,
              ExitMiles = null,
            }
          );
        }
        result.Warnings.AddRange(recipe.Warnings);
        if (recipe.CalculatedAt < result.CalculatedAt)
          result.CalculatedAt = recipe.CalculatedAt;
      }
      result.Legs.Add(leg);
      result.Miles += leg.Miles;
      result.Seconds += leg.Seconds;
    }
    if (result.Miles <= 0)
      return Reject("The complete checked candidate has no route mileage.");
    result.Warnings = result.Warnings.Distinct().ToList();
    return new(result, stations);
  }
}
