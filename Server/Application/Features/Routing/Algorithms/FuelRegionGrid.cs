using Application.Features.Routing.Options;
using Application.Features.Routing.Models;
using Application.Features.Fuel.Queries.GetFuelStations;

namespace Application.Features.Routing.Algorithms;



public sealed class FuelRegionGrid(IReadOnlyList<PricedFuelStation> stations, FuelRegionOptions options, double referencePrice)
{
  // A fixed projected grid keeps a station in the same cell as the truck moves.
  private const double MilesPerDegree = 69.0;
  private static readonly double LongitudeScale = Math.Cos(40 * Math.PI / 180);
  private readonly Dictionary<(int X, int Y), double[]> cellPrices = stations
    .GroupBy(station => GridKey(station.Station.Point, options.CellMiles))
    .ToDictionary(group => group.Key, group => group.Select(station => station.EconomicUsd).Order().ToArray());
  public (int X, int Y) Key(RoutePoint p) => GridKey(p, options.CellMiles);
  private static (int X, int Y) GridKey(RoutePoint p, double cellMiles) =>
    ((int)Math.Floor(p.Longitude * MilesPerDegree * LongitudeScale / cellMiles),
      (int)Math.Floor(p.Latitude * MilesPerDegree / cellMiles));

  public FuelRegionCell Cell(RoutePoint point)
  {
    var key = Key(point);
    var prices = cellPrices.GetValueOrDefault(key) ?? [];
    var median = prices.Length == 0 ? (double?)null : prices[prices.Length / 2];
    return new() { Id = $"{key.X}:{key.Y}", South = key.Y * options.CellMiles / MilesPerDegree,
      North = (key.Y + 1) * options.CellMiles / MilesPerDegree,
      West = key.X * options.CellMiles / (MilesPerDegree * LongitudeScale),
      East = (key.X + 1) * options.CellMiles / (MilesPerDegree * LongitudeScale),
      StationCount = prices.Length, MedianPriceUsd = median,
      Kind = prices.Length == 0 ? "unknown" : prices.Length < options.MinimumStations ? "sparse"
        : median >= referencePrice + options.ExpensivePremiumUsdPerGallon ? "expensive" : "good" };
  }

  public List<FuelRegionCell> Along(RouteGeometry geometry, double progress)
    => Along(geometry.Miles, geometry.At, progress);

  public List<FuelRegionCell> Along(FuelSearchGeometry geometry, double progress, CancellationToken ct = default)
    => Along(geometry.Miles, mile => geometry.At(mile, ct), progress);

  private List<FuelRegionCell> Along(double miles, Func<double, RoutePoint> at, double progress)
  {
    var cells = new Dictionary<string, FuelRegionCell>();
    for (var mile = progress; mile < miles; mile += options.CellMiles / 3)
    { var cell = Cell(at(mile)); cells[cell.Id] = cell; }
    var last = Cell(at(miles)); cells[last.Id] = last;
    return cells.Values.ToList();
  }

  public static List<PricedFuelStation> Prices(IEnumerable<FuelStationDto> stations, TruckRouteProfile p, DateOnly date)
  {
    var result = new List<PricedFuelStation>();
    foreach (var station in stations)
    {
      if (station.Latitude is not { } lat || station.Longitude is not { } lng) continue;
      var point = new RoutePoint((double)lat, (double)lng);
      if (!point.IsValid) continue;
      var prices = new List<PricedFuelStation>();
      foreach (var price in FuelDisplayPrices.EligibleQuotes(station.Discounts, date))
      {
        var cad = price.Currency.Equals("CAD", StringComparison.OrdinalIgnoreCase);
        if (cad && !p.CadToUsd.HasValue) continue;
        if (p.UseIfta && price.PriceAfterIfta is not > 0) continue;
        var factor = cad ? 3.785411784 * p.CadToUsd!.Value : 1;
        var economic = (double)(p.UseIfta ? price.PriceAfterIfta!.Value : price.DiscountPrice);
        prices.Add(new(new() { StationId = station.Id, Name = station.Name, Address = station.Address,
          Point = point, YourPrice = (double)price.DiscountPrice, EconomicPrice = economic,
          Currency = price.Currency, Unit = price.Unit, PriceDate = price.EffectiveFrom },
          (double)price.DiscountPrice * factor, economic * factor));
      }
      if (prices.Count > 0) result.Add(prices.MinBy(x => x.EconomicUsd)!);
    }
    return result.DistinctBy(x => x.Station.StationId).ToList();
  }
}
