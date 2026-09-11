using Application.Features.Routing.Models;
using Application.Features.Routing.Exceptions;
namespace Application.Features.Routing.Algorithms;

public sealed record FuelRouteWaypoint(RoutePoint Point, PlanStop? Stop = null, FuelCandidate? Fuel = null);

public static class FuelRouteVariant
{
  public static List<FuelRouteWaypoint> Waypoints(RoutePoint start, IReadOnlyList<PlanStop> stops,
    IReadOnlyList<FuelCandidate> stations, RouteGeometry reference, double progress, IReadOnlyList<double>? stopMiles = null)
    => Waypoints(start, stops, stations,
      stopMiles ?? stops.Select(stop => reference.Match(stop.Point, progress).Along - progress).ToArray());

  public static List<FuelRouteWaypoint> Waypoints(RoutePoint start, IReadOnlyList<PlanStop> stops,
    IReadOnlyList<FuelCandidate> stations, IReadOnlyList<double> stopMiles)
  {
    if (stopMiles.Count != stops.Count) throw new ArgumentException("Each stop needs its route boundary.", nameof(stopMiles));
    var points = new List<FuelRouteWaypoint> { new(start) };
    var remaining = stations.DistinctBy(x => x.VisitKey).OrderBy(x => x.AlongMiles).ToList();
    for (var i = 0; i < stops.Count; i++)
    {
      var stop = stops[i];
      var end = stopMiles[i];
      foreach (var station in remaining.Where(x => x.LegIndex >= 0 ? x.LegIndex == i : x.AlongMiles <= end).ToList())
      { points.Add(new(station.Station.Point, Fuel: station)); remaining.Remove(station); }
      points.Add(new(stop.Point, Stop: stop));
    }
    return points;
  }

  // Fuel waypoints remain in the geometry, but do not become mandatory dispatch stops.
  public static (TruckRoute Route, List<FuelCandidate> Stations) Collapse(TruckRoute raw, IReadOnlyList<FuelRouteWaypoint> points)
  {
    if (raw.Legs.Count != points.Count - 1) throw new InvalidOperationException("Incomplete route variant.");
    if (!raw.TryGetLegSeconds(out var totalSeconds))
      throw new RoutePlanningException("The checked fuel route has inconsistent timing. The saved fuel plan has been kept.");
    var result = new TruckRoute { CalculatedAt = raw.CalculatedAt, Miles = raw.Miles, Seconds = totalSeconds,
      Points = raw.Points, Warnings = raw.Warnings };
    var stations = new List<FuelCandidate>();
    double along = 0, miles = 0, seconds = 0;
    var geometry = new List<RoutePoint>();
    for (var i = 0; i < raw.Legs.Count; i++)
    {
      var leg = raw.Legs[i]; along += leg.Miles; miles += leg.Miles; seconds += leg.Seconds;
      geometry.AddRange(geometry.Count == 0 ? leg.Points : leg.Points.Skip(1));
      if (points[i + 1].Fuel is { } fuel)
        stations.Add(fuel with { Station = CheckedStation(fuel.Station), AlongMiles = along,
          ExtraInMiles = 0, ExtraOutMiles = 0, EntryMiles = null, ExitMiles = null });
      if (points[i + 1].Stop is not null)
      { result.Legs.Add(new(miles, seconds, geometry)); miles = seconds = 0; geometry = []; }
    }
    return (result, stations);
  }

  // The checked route already contains access time; keep estimates only on the original candidate.
  internal static FuelPlanStop CheckedStation(FuelPlanStop source) => new()
  {
    Number = source.Number, VisitKey = source.VisitKey, DispatchId = source.DispatchId, BeforeStopId = source.BeforeStopId,
    CashUsdPerGallon = source.CashUsdPerGallon, EconomicUsdPerGallon = source.EconomicUsdPerGallon,
    CurrentRouteMile = source.CurrentRouteMile, StationId = source.StationId, Name = source.Name, Address = source.Address,
    Point = source.Point, MilesAhead = source.MilesAhead, ArrivalGallons = source.ArrivalGallons,
    BuyGallons = source.BuyGallons, DepartureGallons = source.DepartureGallons, FillToTarget = source.FillToTarget,
    YourPrice = source.YourPrice, EconomicPrice = source.EconomicPrice, Currency = source.Currency, Unit = source.Unit,
    DetourMiles = source.DetourMiles, DetourMinutes = 0, PriceDate = source.PriceDate
  };
}
