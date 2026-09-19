using Application.Features.Eta.Interfaces;
using Application.Features.Routing.Models;

namespace Application.Features.Routing.Algorithms;

public sealed class FuelAccessCountries(IRouteRegionLookup regions)
{
  private const int MaximumCachedPoints = 4096;
  private readonly Dictionary<RoutePoint, string> countries = [];

  public bool Matches(RoutePoint road, FuelPlanStop station)
  {
    var origin = Country(road);
    var destination = Country(station.Point);
    return origin.Length > 0
      && origin == destination
      && (
        string.IsNullOrWhiteSpace(station.Country)
        || Normalize(station.Country) == destination
      );
  }

  private string Country(RoutePoint point)
  {
    if (!point.IsValid)
      return "";
    if (countries.TryGetValue(point, out var country))
      return country;
    country = Normalize(regions.Find(point).Country);
    if (countries.Count < MaximumCachedPoints)
      countries.Add(point, country);
    return country;
  }

  private static string Normalize(string country) =>
    country.Trim().ToUpperInvariant() switch
    {
      "USA" or "UNITED STATES" or "UNITED STATES OF AMERICA" => "US",
      "CAN" or "CANADA" => "CA",
      var value => value,
    };
}
