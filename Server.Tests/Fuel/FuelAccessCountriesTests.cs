using Application.Features.Eta.Interfaces;
using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Models;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Unit")]
public sealed class FuelAccessCountriesTests
{
  [Theory]
  [InlineData("", 48, true)]
  [InlineData("US", 48, true)]
  [InlineData("USA", 48, true)]
  [InlineData("United States", 48, true)]
  [InlineData("CA", 48, false)]
  [InlineData("US", 50, false)]
  [InlineData("", 50, false)]
  [InlineData("CA", 50, false)]
  public void CountryAtAccessPointMustMatchGeographyAndStationMetadata(
    string stationCountry,
    double latitude,
    bool allowed
  )
  {
    var countries = new FuelAccessCountries(new Regions());
    var station = new FuelPlanStop
    {
      Country = stationCountry,
      Point = new(latitude, -100),
    };
    Assert.Equal(allowed, countries.Matches(new(48, -100), station));
  }

  [Fact]
  public void CrossBorderItineraryKeepsEachStationOnItsOwnRoadSection()
  {
    var countries = new FuelAccessCountries(new Regions());
    var canadian = new FuelPlanStop
    {
      Country = "Canada",
      Point = new(50, -100),
    };
    Assert.True(countries.Matches(new(50, -101), canadian));
    Assert.False(countries.Matches(new(48, -101), canadian));
  }

  [Fact]
  public void UnknownGeographyCannotCertifyAnEstimatedStationAccess()
  {
    var countries = new FuelAccessCountries(new Regions());
    var station = new FuelPlanStop { Country = "US", Point = new(48, -100) };
    Assert.False(countries.Matches(new(-20, -100), station));
    Assert.False(
      countries.Matches(
        new(48, -100),
        new() { Country = "US", Point = new(-20, -100) }
      )
    );
  }

  [Fact]
  public void RepeatedCandidatesReuseTheirGeographicCountryLookups()
  {
    var regions = new Regions();
    var countries = new FuelAccessCountries(regions);
    var road = new RoutePoint(50, -101);
    var station = new FuelPlanStop { Point = new(50, -100) };
    for (var i = 0; i < 100; i++)
      Assert.True(countries.Matches(road, station));
    Assert.Equal(2, regions.Reads);
  }

  private sealed class Regions : IRouteRegionLookup
  {
    public int Reads { get; private set; }

    public RouteRegion Find(RoutePoint point)
    {
      Reads++;
      return new(
        point.Latitude < 0 ? ""
          : point.Latitude < 49 ? "US"
          : "CA",
        "",
        false
      );
    }
  }
}
