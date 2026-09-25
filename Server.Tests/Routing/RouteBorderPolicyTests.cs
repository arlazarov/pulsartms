using System.Globalization;
using System.Net;
using Application.Features.Routing.Services.Routes;
using Domain.Models.Routing;
using Domain.Rules.Ports;
using Domain.Rules.Routing;
using Infrastructure.Integrations.GeoTimeZone;
using Infrastructure.Integrations.TomTom;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Server.Tests.Routing;

// Truck 11007's next load, AMF1414 (September 25): Ticonderoga, New York to
// De Pere, Wisconsin, both stops correctly placed in the United States. Its
// saved road went through Ontario (2,801 of 14,178 points, entering at the
// Niagara bridges): the provider's fastest truck road, asked for with no
// border policy. A road whose points are all in one country is now asked
// to avoid border crossings; work that has a point in another country, or
// one of unknown country, is asked for exactly as before.
[Trait("Category", "Routing")]
[Trait("Kind", "Integration")]
public sealed class RouteBorderPolicyTests
{
  private static readonly RoutePoint Amsterdam = new(42.9379051, -74.2410061);
  private static readonly RoutePoint Ticonderoga = new(43.8486707, -73.4234531);
  private static readonly RoutePoint DePere = new(44.4488805, -88.0603806);
  private static readonly RoutePoint Toronto = new(43.6532, -79.3832);
  private static readonly RoutePoint Detroit = new(42.3314, -83.0458);
  private static readonly RoutePoint London = new(42.9849, -81.2453);

  [Fact]
  public async Task ADomesticLoadIsAskedToStayInItsCountry()
  {
    await using var fixture = await Fixture.CreateAsync();

    await fixture.Provider.CalculateAsync(
      [Amsterdam, Ticonderoga, DePere],
      Profile(),
      default
    );
    await fixture.Provider.CalculateAlternativesAsync(
      [Ticonderoga, DePere],
      Profile(),
      default
    );

    Assert.Equal(2, fixture.Handler.Queries.Count);
    Assert.All(
      fixture.Handler.Queries,
      query => Assert.Contains("avoid=borderCrossings", query)
    );
  }

  [Fact]
  public async Task CrossBorderWorkAndAChosenWaypointAbroadKeepTheirRoad()
  {
    await using var fixture = await Fixture.CreateAsync();

    // A load from Toronto to Detroit crosses by its nature.
    await fixture.Provider.CalculateAsync(
      [Toronto, Detroit],
      Profile(),
      default
    );
    // A dispatcher's own waypoint in Ontario between two US stops.
    await fixture.Provider.CalculateAsync(
      [Ticonderoga, London, DePere],
      Profile(),
      default
    );

    Assert.Equal(2, fixture.Handler.Queries.Count);
    Assert.All(
      fixture.Handler.Queries,
      query => Assert.DoesNotContain("avoid=", query)
    );
  }

  [Fact]
  public void OnlyEveryPointInOneKnownCountryKeepsTheRoadThere()
  {
    RouteRegion In(string country) => new(country, "", false);

    Assert.True(RouteBorderPolicy.KeepsToOneCountry([In("US"), In("US")]));
    Assert.True(RouteBorderPolicy.KeepsToOneCountry([In("CA"), In("CA")]));
    Assert.False(RouteBorderPolicy.KeepsToOneCountry([In("US"), In("CA")]));
    // A point whose country is not known leaves the road to the provider.
    Assert.False(RouteBorderPolicy.KeepsToOneCountry([In("US"), In("")]));
    Assert.False(RouteBorderPolicy.KeepsToOneCountry([In("US")]));
    Assert.False(RouteBorderPolicy.KeepsToOneCountry([]));
    // The lookup the provider uses places the incident's stops in the US
    // and the crossing's Ontario side in Canada.
    var regions = new RouteRegionLookup();
    Assert.Equal(
      ["US", "US", "US", "CA", "CA"],
      new[] { Amsterdam, Ticonderoga, DePere, Toronto, London }.Select(point =>
        regions.Find(point).Country
      )
    );
  }

  private static TruckRouteProfile Profile() =>
    new()
    {
      Confirmed = true,
      HeightFeet = 13.5,
      WidthFeet = 8.5,
      LengthFeet = 70,
      WeightPounds = 80000,
      Axles = 5,
      AxleWeightPounds = 17000,
    };

  private sealed class Fixture : IAsyncDisposable
  {
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly HttpClient _client;

    private Fixture(SqliteConnection connection, AppDbContext db)
    {
      _connection = connection;
      _db = db;
      _client = new HttpClient(Handler);
      Provider = new TomTomRoutingProvider(
        _client,
        new ConfigurationBuilder()
          .AddInMemoryCollection(
            new Dictionary<string, string?> { ["TomTom:ApiKey"] = "test-only" }
          )
          .Build(),
        db,
        new RouteRequestValidator(),
        new UnusedAddressGeocoder(),
        new RouteSectionValidator(),
        new RouteRegionLookup()
      );
    }

    public RecordingTomTom Handler { get; } = new();
    public TomTomRoutingProvider Provider { get; }

    public static async Task<Fixture> CreateAsync()
    {
      var connection = new SqliteConnection("Data Source=:memory:");
      await connection.OpenAsync();
      var db = new AppDbContext(
        new DbContextOptionsBuilder<AppDbContext>()
          .UseSqlite(connection)
          .Options
      );
      await db.Database.EnsureCreatedAsync();
      return new(connection, db);
    }

    public async ValueTask DisposeAsync()
    {
      _client.Dispose();
      Handler.Dispose();
      await _db.DisposeAsync();
      await _connection.DisposeAsync();
    }
  }

  // Answers each request with a straight road through the points it names,
  // one leg between each pair, and keeps the request's query.
  private sealed class RecordingTomTom : HttpMessageHandler
  {
    public List<string> Queries { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request,
      CancellationToken cancellationToken
    )
    {
      var uri = request.RequestUri!;
      Queries.Add(uri.Query);
      var path = Uri.UnescapeDataString(uri.AbsolutePath);
      var points = path.Split('/')[^2]
        .Split(':')
        .Select(pair => pair.Split(','))
        .Select(pair => $"{{\"latitude\":{pair[0]},\"longitude\":{pair[1]}}}")
        .ToArray();
      var legs = string.Join(
        ",",
        points
          .Zip(points.Skip(1))
          .Select(leg =>
            "{\"summary\":{\"lengthInMeters\":1000,\"travelTimeInSeconds\":60},"
            + $"\"points\":[{leg.First},{leg.Second}]}}"
          )
      );
      var legCount = points.Length - 1;
      var meters = (1000 * legCount).ToString(CultureInfo.InvariantCulture);
      var seconds = (60 * legCount).ToString(CultureInfo.InvariantCulture);
      return Task.FromResult(
        new HttpResponseMessage(HttpStatusCode.OK)
        {
          Content = new StringContent(
            "{\"routes\":[{\"summary\":{\"lengthInMeters\":"
              + meters
              + ",\"travelTimeInSeconds\":"
              + seconds
              + "},\"legs\":["
              + legs
              + "]}]}"
          ),
        }
      );
    }
  }
}
