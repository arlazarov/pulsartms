using System.Globalization;
using Client.Models;
using Client.Models.DTO.Fleet;
using Client.Models.DTO.Planning;
using Client.Services;
using Microsoft.AspNetCore.Components;

namespace Client.Pages.FleetMap;

// The selected day's stations as a list beside the map: searched by name,
// city or address, narrowed to the plan's stops or a brand, with the
// price the map shows and, for a planned stop, its number and how far
// ahead it is. The stations are the ones the map already loaded; nothing
// here asks the server for more, and a station off the plan has no route
// distance because the server has not measured one.
public partial class FleetFuelStations
{
  // A list of hundreds of stations is not read, it is searched.
  private const int ShownLimit = 50;

  [CascadingParameter]
  public DisplayUnits Units { get; set; } = DisplayUnits.Default;

  [Parameter]
  public IReadOnlyList<FuelStationMapDto>? Stations { get; set; }

  [Parameter]
  public string? Error { get; set; }

  [Parameter]
  public DateOnly Date { get; set; }

  [Parameter]
  public bool UseIfta { get; set; }

  [Parameter]
  public FuelPlan? Plan { get; set; }

  [Parameter]
  public EventCallback<Guid> View { get; set; }

  [Parameter]
  public EventCallback Back { get; set; }

  private string _query = "";
  private string _scope = "all";
  private string _brand = "";

  private sealed record Row(
    Guid Id,
    string Name,
    string Address,
    string Price,
    FuelPlanStop? Planned
  );

  private string PriceLabel => UseIfta ? "price after IFTA" : "your price";

  // Built once per change of what they are built from, not once per read:
  // a render reads the rows three times, and there are hundreds.
  private object? _brandsFrom;
  private IReadOnlyList<string> _brands = [];
  private object? _rowsFrom;
  private List<Row> _rows = [];

  private IReadOnlyList<string> Brands
  {
    get
    {
      if (ReferenceEquals(_brandsFrom, Stations))
        return _brands;
      _brandsFrom = Stations;
      return _brands = (Stations ?? [])
        .Select(station => Brand(station.Name))
        .Where(brand => brand.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Order(StringComparer.OrdinalIgnoreCase)
        .ToArray();
    }
  }

  private List<Row> Rows
  {
    get
    {
      var from = (Stations, Plan, _query, _scope, _brand, Date, UseIfta);
      if (_rowsFrom is { } built && built.Equals(from))
        return _rows;
      _rowsFrom = from;
      return _rows = BuildRows();
    }
  }

  private List<Row> BuildRows()
  {
      var planned = (Plan?.Stops ?? [])
        .GroupBy(stop => stop.StationId)
        .ToDictionary(group => group.Key, group => group.First());
      var query = _query.Trim();
      return (Stations ?? [])
        .Where(station => _scope != "planned" || planned.ContainsKey(station.Id))
        .Where(station =>
          _brand.Length == 0
          || string.Equals(
            Brand(station.Name),
            _brand,
            StringComparison.OrdinalIgnoreCase
          )
        )
        .Where(station =>
          query.Length == 0
          || new[] { station.Name, station.City, station.Region, station.Address }
            .Any(text =>
              text.Contains(query, StringComparison.OrdinalIgnoreCase)
            )
        )
        .Select(station => new Row(
          station.Id,
          station.Name,
          Address(station),
          Price(station),
          planned.GetValueOrDefault(station.Id)
        ))
        .OrderBy(row => row.Planned?.Number ?? int.MaxValue)
        .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
        .ToList();
  }

  // The chain a station belongs to, as its name says it: "LOVES #504".
  private static string Brand(string name)
  {
    var trimmed = name.Trim();
    var end = trimmed.IndexOfAny([' ', '#']);
    return end < 0 ? trimmed : trimmed[..end].Trim();
  }

  private static string Address(FuelStationMapDto station)
  {
    var lines = StopAddressLines.Create(station.Address);
    return lines.Locality.Length > 0
      ? $"{lines.Street}, {lines.Locality}"
      : lines.Street.Length > 0
        ? lines.Street
        : string.Join(", ", new[] { station.City, station.Region }
          .Where(part => !string.IsNullOrWhiteSpace(part)));
  }

  // The discount the map reads for this day, as selectStationPrices does:
  // the IFTA one when the account prices after IFTA, the cash one
  // otherwise, and only while it is in effect on the selected day.
  private string Price(FuelStationMapDto station)
  {
    var discount = UseIfta
      ? station.IftaDiscount ?? station.CashDiscount
      : station.CashDiscount;
    if (
      discount is null
      || Date < discount.EffectiveFrom
      || Date > discount.EffectiveTo
    )
      return "—";
    var price = UseIfta ? discount.PriceAfterIfta : discount.DiscountPrice;
    return price is > 0
      ? $"{price.Value.ToString("N3", CultureInfo.InvariantCulture)} {discount.Currency} / {discount.Unit}"
      : "—";
  }

  private void OnQuery(ChangeEventArgs args) =>
    _query = args.Value?.ToString() ?? "";

  private void OnScope(ChangeEventArgs args) =>
    _scope = args.Value?.ToString() == "planned" ? "planned" : "all";

  private void OnBrand(ChangeEventArgs args) =>
    _brand = args.Value?.ToString() ?? "";
}
