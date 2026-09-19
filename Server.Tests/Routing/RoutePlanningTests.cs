using System.Net;
using System.Text.Json;
using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services.Routes;
using Infrastructure.Integrations.TomTom;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Server.Tests.Routing;

[Trait("Category", "Routing")]
public class RoutePlanningTests
{
  private static TruckRouteProfile Profile() =>
    new()
    {
      Confirmed = true,
      TankGallons = 100,
      Mpg = 5,
      ReserveGallons = 10,
      FillPercent = 100,
      StopCostUsd = 20,
    };

  private static FuelCandidate Station(
    string name,
    double mile,
    double price,
    double detour = 0
  ) =>
    new(
      new FuelPlanStop
      {
        StationId = Guid.NewGuid(),
        Name = name,
        YourPrice = price,
        Currency = "USD",
        Unit = "US gal",
      },
      mile,
      detour / 2,
      detour / 2,
      price,
      price
    );

  [Fact]
  public void NoStopIfFuelCanReachDestinationWithReserve()
  {
    var plan = FuelOptimizer.Optimize(
      100,
      50,
      Profile(),
      [Station("Cheap", 20, 2)],
      1,
      false
    );
    Assert.Empty(plan.Stops);
    Assert.Equal(30, plan.ArrivalGallons);
  }

  [Fact]
  public void BuysOnlyEnoughAtExpensiveStationToReachCheaperStation()
  {
    var p = Profile();
    p.StopCostUsd = 0;
    var plan = FuelOptimizer.Optimize(
      600,
      30,
      p,
      [Station("Expensive", 90, 5), Station("Cheap", 200, 2)],
      1,
      false
    );
    Assert.Equal(2, plan.Stops.Count);
    Assert.Equal("Expensive", plan.Stops[0].Name);
    Assert.Equal(25, plan.Stops[0].BuyGallons);
    Assert.Equal("Cheap", plan.Stops[1].Name);
    Assert.Equal(80, plan.Stops[1].BuyGallons);
    Assert.True(plan.ArrivalGallons >= 10);
  }

  [Fact]
  public void StopPenaltyAvoidsExtraStopForTinyDiscount()
  {
    var p = Profile();
    p.StopCostUsd = 50;
    var plan = FuelOptimizer.Optimize(
      350,
      30,
      p,
      [Station("First", 80, 3), Station("Slightly cheaper", 170, 2.99)],
      1,
      false
    );
    Assert.Single(plan.Stops);
    Assert.Equal("First", plan.Stops[0].Name);
  }

  [Fact]
  public void DetourFuelMayUseReserveButCannotInventFuel()
  {
    var plan = FuelOptimizer.Optimize(
      300,
      30,
      Profile(),
      [Station("Reachable below reserve", 95, 3, 20)],
      1,
      false
    );
    var stop = Assert.Single(plan.Stops);
    Assert.InRange(stop.ArrivalGallons, 0, Profile().ReserveGallons);
    Assert.Contains("Below reserve", stop.Warning);
    Assert.True(plan.ArrivalGallons >= Profile().ReserveGallons);
  }

  [Fact]
  public void UnreachableDestinationReturnsNoMisleadingPartialPlan()
  {
    Assert.Throws<RoutePlanningException>(
      () =>
        FuelOptimizer.Optimize(
          1200,
          30,
          Profile(),
          [Station("Only", 80, 3)],
          1,
          false
        )
    );
  }

  [Fact]
  public void FuelPlanNeverExceedsCapacityOrDropsBelowReserve()
  {
    var p = Profile();
    var plan = FuelOptimizer.Optimize(
      1100,
      60.8,
      p,
      [
        Station("A", 180, 3.9, 2),
        Station("B", 380, 3, 4),
        Station("C", 650, 4),
        Station("D", 900, 3.1),
      ],
      1,
      false
    );
    Assert.All(
      plan.Stops,
      x =>
      {
        Assert.InRange(x.DepartureGallons, 10, 100);
        Assert.True(x.ArrivalGallons >= 10);
      }
    );
    Assert.True(plan.ArrivalGallons >= 10);
    var fuelUsed = (1100 + plan.Stops.Sum(x => x.DetourMiles)) / 5;
    Assert.True(
      plan.StartingGallons + plan.PurchaseGallons - fuelUsed
        >= plan.ArrivalGallons
    );
  }

  [Fact]
  public void GeometryUsesRoadLengthNotStraightLineDistance()
  {
    var route = new TruckRoute
    {
      Miles = 100,
      Legs = [new(100, 7200, [new(40, -80), new(40, -79)])],
    };
    var match = new RouteGeometry(route).Match(new(40, -79.5));
    Assert.Equal(50, match.Along, 5);
    Assert.Equal(0, match.Away, 5);
  }

  [Fact]
  public void InvalidProfileOrMissingCapacityCannotProduceFuelRecommendation()
  {
    Assert.NotNull(new TruckRouteProfile().Validate());
    Assert.NotNull(new TruckRouteProfile { Confirmed = true }.Validate(true));
    var profile = Profile();
    profile.Mpg = double.NaN;
    Assert.NotNull(profile.Validate(true));
  }

  [Fact]
  public async Task ProviderCachesAcrossInstancesAndPassesTruckDimensions()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    var options = new DbContextOptionsBuilder<AppDbContext>()
      .UseSqlite(connection)
      .Options;
    await using var db = new AppDbContext(options);
    await db.Database.EnsureCreatedAsync();
    var handler = new FakeHandler();
    var config = new ConfigurationBuilder()
      .AddInMemoryCollection(
        new Dictionary<string, string?> { ["TomTom:ApiKey"] = "test-secret" }
      )
      .Build();
    var provider = new TomTomRoutingProvider(
      new HttpClient(handler),
      config,
      db,
      new RouteRequestValidator(),
      new UnusedAddressGeocoder(),
      new RouteSectionValidator()
    );
    var points = new List<RoutePoint> { new(40, -80), new(40, -79) };
    var first = await provider.CalculateAsync(points, Profile(), default);
    await using var db2 = new AppDbContext(options);
    var second = await new TomTomRoutingProvider(
      new HttpClient(handler),
      config,
      db2,
      new RouteRequestValidator(),
      new UnusedAddressGeocoder(),
      new RouteSectionValidator()
    ).CalculateAsync(points, Profile(), default);
    Assert.Equal(1, handler.Calls);
    Assert.Equal(first.Miles, second.Miles);
    Assert.Contains("travelMode=truck", handler.Query);
    Assert.Contains("vehicleCommercial=true", handler.Query);
    Assert.Contains("vehicleHeight=4.1148", handler.Query);
    Assert.Contains("vehicleWeight=36288", handler.Query);
    Assert.DoesNotContain(
      "test-secret",
      (await db.RoutingApiCalls.SingleAsync()).ResultJson!
    );
  }

  [Fact]
  public async Task ProviderDailyLimitBlocksBeforeCallingTomTom()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    var config = new ConfigurationBuilder()
      .AddInMemoryCollection(
        new Dictionary<string, string?>
        {
          ["TomTom:ApiKey"] = "test",
          ["TomTom:DailyRequestLimit"] = "1",
        }
      )
      .Build();
    var handler = new FakeHandler();
    var provider = new TomTomRoutingProvider(
      new HttpClient(handler),
      config,
      db,
      new RouteRequestValidator(),
      new UnusedAddressGeocoder(),
      new RouteSectionValidator()
    );
    await provider.CalculateAsync(
      [new(40, -80), new(40, -79)],
      Profile(),
      default
    );
    await Assert.ThrowsAsync<RoutePlanningException>(
      () =>
        provider.CalculateAsync(
          [new(41, -80), new(41, -79)],
          Profile(),
          default
        )
    );
    Assert.Equal(1, handler.Calls);
  }

  [Fact]
  public async Task ProviderPreservesUnsupportedSectionsWithCachedWarnings()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    var handler = new FakeHandler { Restricted = true };
    var config = new ConfigurationBuilder()
      .AddInMemoryCollection(
        new Dictionary<string, string?> { ["TomTom:ApiKey"] = "test" }
      )
      .Build();
    var provider = new TomTomRoutingProvider(
      new HttpClient(handler),
      config,
      db,
      new RouteRequestValidator(),
      new UnusedAddressGeocoder(),
      new RouteSectionValidator()
    );
    var route = await provider.CalculateAsync(
      [new(40, -80), new(40, -79)],
      Profile(),
      default
    );
    Assert.Single(route.Warnings);
    Assert.NotNull((await db.RoutingApiCalls.SingleAsync()).ResultJson);
    var cached = await provider.CalculateAsync(
      [new(40, -80), new(40, -79)],
      Profile(),
      default
    );
    Assert.Equal(route.Warnings, cached.Warnings);
    Assert.Equal(route.Miles, cached.Miles);
    Assert.Equal(1, handler.Calls);
  }

  [Theory]
  [InlineData(-79.99995, 1, 2, false)]
  [InlineData(-79.99, 1, 2, false)]
  [InlineData(-79.99995, 0, 1, false)]
  [InlineData(-79.99995, 1, 2, true)]
  [InlineData(-79.99995, 2, 1, false)]
  public async Task SectionWarningsDoNotAlterProviderMileageOrGeometry(
    double endLongitude,
    int start,
    int end,
    bool restriction
  )
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    var points = new[]
    {
      new { latitude = 39.99, longitude = -80d },
      new { latitude = 40d, longitude = -80d },
      new { latitude = 40d, longitude = endLongitude },
    };
    var summary = new { lengthInMeters = 1200, travelTimeInSeconds = 120 };
    var sections = new object[]
    {
      new
      {
        sectionType = "TRAVEL_MODE",
        travelMode = "truck",
        startPointIndex = 0,
        endPointIndex = 1,
      },
      new
      {
        sectionType = "TRAVEL_MODE",
        travelMode = "other",
        startPointIndex = start,
        endPointIndex = end,
        simpleCategory = restriction ? "TRUCK_RESTRICTIONS" : "",
      },
    };
    var handler = new FakeHandler
    {
      Response = JsonSerializer.Serialize(
        new
        {
          routes = new[]
          {
            new
            {
              summary,
              legs = new[] { new { summary, points } },
              sections,
            },
          },
        }
      ),
    };
    var config = new ConfigurationBuilder()
      .AddInMemoryCollection(
        new Dictionary<string, string?> { ["TomTom:ApiKey"] = "test" }
      )
      .Build();
    var provider = new TomTomRoutingProvider(
      new HttpClient(handler),
      config,
      db,
      new RouteRequestValidator(),
      new UnusedAddressGeocoder(),
      new RouteSectionValidator()
    );
    var input = new[]
    {
      new RoutePoint(39.99, -80),
      new RoutePoint(40, endLongitude),
    };
    var route = await provider.CalculateAsync(input, Profile(), default);
    Assert.Equal(3, route.Points.Count);
    Assert.Equal(new RoutePoint(40, endLongitude), route.Legs[0].Points.Last());
    Assert.Equal(1200 / 1609.344, route.Miles);
    Assert.Equal(120, route.Seconds);
    Assert.Equal(restriction ? 2 : 1, route.Warnings.Count);
  }

  private sealed class FakeHandler : HttpMessageHandler
  {
    public int Calls;
    public string Query = "";
    public bool Restricted;
    public string? Response;

    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request,
      CancellationToken ct
    )
    {
      Calls++;
      Query = request.RequestUri!.Query;
      var response = """
        {"routes":[{"summary":{"lengthInMeters":160934,"travelTimeInSeconds":7200},"legs":[{"summary":{"lengthInMeters":160934,"travelTimeInSeconds":7200},"points":[{"latitude":40,"longitude":-80},{"latitude":40,"longitude":-79}]}],"sections":[{"sectionType":"TRAVEL_MODE","travelMode":"MODE"}]}]}
        """.Replace("MODE\"", Restricted ? "other\"" : "truck\"");
      return Task.FromResult(
        new HttpResponseMessage(HttpStatusCode.OK)
        {
          Content = new StringContent(Response ?? response),
        }
      );
    }
  }
}
