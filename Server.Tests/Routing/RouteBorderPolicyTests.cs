using System.Globalization;
using System.Net;
using Application.Features.Routing.Services.Routes;
using Domain.Models.Routing;
using Domain.Rules;
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
  private static readonly RoutePoint Buffalo = new(42.8864, -78.8784);

  // A road that stays in the US even drawn in straight lines.
  private static readonly RoutePoint[] InTheUs =
  [
    new(39.7684, -86.1581),
    new(41.8781, -87.6298),
  ];

  // The same, by way of the sea off Boston, where no country is known.
  private static readonly RoutePoint[] Offshore =
  [
    new(42.3601, -71.0589),
    new(41, -66),
    new(42.3601, -71.0589),
    .. InTheUs,
  ];

  [Fact]
  public async Task ADomesticLoadIsAskedToStayInItsCountry()
  {
    await using var fixture = await Fixture.CreateAsync();
    fixture.Handler.Detours = [InTheUs];

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

  // The border policy is a preference: TomTom may answer with a road
  // through Ontario anyway. That road is not used or passed on as domestic;
  // planning fails with the reason, and asking again reuses the cached
  // answer instead of buying another.
  [Fact]
  public async Task ADomesticRoadTheProviderSendsAbroadIsNotUsed()
  {
    await using var fixture = await Fixture.CreateAsync();
    fixture.Handler.Detours =
    [
      [London],
    ];

    for (var attempt = 0; attempt < 2; attempt++)
    {
      var error = await Assert.ThrowsAsync<RoutePlanningException>(
        () =>
          fixture.Provider.CalculateAsync(
            [Ticonderoga, DePere],
            Profile(),
            default
          )
      );
      Assert.Contains("leaves US through CA", error.Message);
    }

    Assert.Contains("avoid=borderCrossings", fixture.Handler.Queries.Single());
  }

  // Of the alternatives, only those that stay in the country are offered;
  // when none does, the choice fails with the reason.
  [Fact]
  public async Task OnlyDomesticAlternativesAreOffered()
  {
    await using var fixture = await Fixture.CreateAsync();
    fixture.Handler.Detours =
    [
      [London],
      InTheUs,
    ];

    var roads = await fixture.Provider.CalculateAlternativesAsync(
      [Ticonderoga, DePere],
      Profile(),
      default
    );

    Assert.Equal(InTheUs[0], Assert.Single(roads).Legs[0].Points[1]);
    fixture.Handler.Detours =
    [
      [London],
    ];
    await Assert.ThrowsAsync<RoutePlanningException>(
      () =>
        fixture.Provider.CalculateAlternativesAsync(
          [Amsterdam, DePere],
          Profile(),
          default
        )
    );
  }

  // A road through Ontario is right for a load that crosses and for a
  // dispatcher's waypoint there. A point the lookup cannot place (out at
  // sea) does not refuse a domestic road, but neither does it pass: the
  // road's verdict is unknown (below).
  [Fact]
  public async Task CrossBorderRoadsAndUnplacedPointsAreKept()
  {
    await using var fixture = await Fixture.CreateAsync();
    fixture.Handler.Detours =
    [
      [London],
    ];

    await fixture.Provider.CalculateAsync(
      [Toronto, Detroit],
      Profile(),
      default
    );
    await fixture.Provider.CalculateAsync(
      [Ticonderoga, London, DePere],
      Profile(),
      default
    );
    fixture.Handler.Detours = [Offshore];
    await fixture.Provider.CalculateAsync(
      [Ticonderoga, DePere],
      Profile(),
      default
    );

    Assert.Equal("", new RouteRegionLookup().Find(Offshore[1]).Country);
    Assert.Equal(3, fixture.Handler.Queries.Count);
  }

  // The reviewed gap: a check of 400 evenly spaced points would step over
  // a short excursion on a road of AMF1414's size (14,178 points). Every
  // point is looked up: here only the one point between two such samples
  // is abroad, and it is found; the same road without it stays.
  [Fact]
  public void EveryPointOfALongRoadIsLookedUp()
  {
    var path = Dense(14_000, new(40, -75), new(40, -95));
    var step = (path.Count + 399) / 400;
    var excursion = new RoutePoint(40.3, -80);
    var regions = new OnePointAbroad(excursion);

    Assert.Equal(
      BorderCheck.Stays,
      RouteBorderPolicy.Check(Straight(path), regions, default).Check
    );
    path[7 * step + step / 2] = excursion;
    var verdict = RouteBorderPolicy.Check(Straight(path), regions, default);

    Assert.Equal(BorderCheck.Leaves, verdict.Check);
    Assert.Equal(
      ("US", "CA", excursion),
      (verdict.Country, verdict.Entered, verdict.At)
    );
  }

  // Two points of the road far apart: Buffalo to Detroit in a straight
  // line runs through Ontario although both ends are in the US. The
  // stretch between them is looked up too, at least every kilometre.
  [Fact]
  public void AStraightStretchBetweenTwoPointsIsLookedUpToo()
  {
    var verdict = RouteBorderPolicy.Check(
      Straight([Buffalo, Detroit]),
      [Buffalo, Detroit],
      new RouteRegionLookup(),
      default
    );

    Assert.Equal(BorderCheck.Leaves, verdict.Check);
    Assert.Equal("CA", verdict.Entered);
  }

  // What the check cannot place is unknown, never stays: a point off every
  // country, a bad coordinate, a road without points. Unknown is stored as
  // such and reviewed, not counted as clean.
  [Fact]
  public void ARoadThatCannotBeFullyPlacedIsUnknown()
  {
    var regions = new RouteRegionLookup();
    Assert.Equal(
      BorderCheck.Unknown,
      RouteBorderPolicy
        .Check(Straight([Ticonderoga, .. Offshore, DePere]), regions, default)
        .Check
    );
    Assert.Equal(
      BorderCheck.Unknown,
      RouteBorderPolicy
        .Check(
          Straight([Ticonderoga, new(double.NaN, 0), DePere]),
          regions,
          default
        )
        .Check
    );
    Assert.Equal(
      BorderCheck.Unknown,
      RouteBorderPolicy.Check(new TruckRoute(), regions, default).Check
    );
    Assert.Equal(
      "unknown",
      RouteBorderPolicy.Check(new TruckRoute(), regions, default).Stored
    );
    // Work not in one country is not judged, whatever its road does.
    Assert.Equal(
      "n/a",
      RouteBorderPolicy
        .Check(Straight([Toronto, London, Detroit]), regions, default)
        .Stored
    );
  }

  // A coordinate that is no place on Earth leaves the road unknown. So
  // does geometry that would need more lookups than the budget - here
  // points a continent apart, 200 times - and it costs no more than the
  // budget. Without it this road would pass as staying after a million.
  [Fact]
  public void BadOrOversizedGeometryIsUnknownWithinTheBudget()
  {
    var regions = new CountingUs();
    foreach (var bad in new RoutePoint[] { new(95, -80), new(43, -200) })
      Assert.Equal(
        BorderCheck.Unknown,
        RouteBorderPolicy
          .Check(Straight([Ticonderoga, bad, DePere]), regions, default)
          .Check
      );
    var zigzag = Enumerable
      .Range(0, 200)
      .Select(index =>
        index % 2 == 0 ? new RoutePoint(25, -70) : new RoutePoint(48, -124)
      )
      .ToList();
    regions.Lookups = 0;

    var verdict = RouteBorderPolicy.Check(Straight(zigzag), regions, default);

    Assert.Equal(BorderCheck.Unknown, verdict.Check);
    Assert.InRange(regions.Lookups, 1, RouteBorderPolicy.MaximumLookups + 2);
    Assert.Throws<OperationCanceledException>(
      () =>
        RouteBorderPolicy.Check(
          Straight(zigzag),
          regions,
          new CancellationToken(canceled: true)
        )
    );
  }

  // Places everything in the US and counts the lookups.
  private sealed class CountingUs : IRouteRegionLookup
  {
    public int Lookups { get; set; }

    public RouteRegion Find(RoutePoint point)
    {
      Lookups++;
      return new("US", "", false);
    }
  }

  // Places everything in the US except one exact point, in Canada.
  private sealed class OnePointAbroad(RoutePoint abroad) : IRouteRegionLookup
  {
    public RouteRegion Find(RoutePoint point) =>
      new(point == abroad ? "CA" : "US", "", false);
  }

  // One leg through the given points, in order.
  private static TruckRoute Straight(IReadOnlyList<RoutePoint> points)
  {
    var leg = new RouteLeg(900, 36_000, [.. points]);
    return new()
    {
      Miles = 900,
      Seconds = 36_000,
      Legs = [leg],
      Points = [.. points],
    };
  }

  // About `count` points evenly along the corners given.
  private static List<RoutePoint> Dense(int count, params RoutePoint[] corners)
  {
    var each = count / (corners.Length - 1);
    var points = new List<RoutePoint>();
    foreach (var (from, to) in corners.Zip(corners.Skip(1)))
      for (var index = 0; index < each; index++)
        points.Add(
          new(
            from.Latitude + (to.Latitude - from.Latitude) * index / each,
            from.Longitude + (to.Longitude - from.Longitude) * index / each
          )
        );
    points.Add(corners[^1]);
    return points;
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
  // one leg between each pair, and keeps the request's query. Each detour
  // is one road offered, its last leg bent through those points whatever
  // the request asked to avoid.
  private sealed class RecordingTomTom : HttpMessageHandler
  {
    public List<string> Queries { get; } = [];
    public RoutePoint[][] Detours { get; set; } =
      [
        [],
      ];

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
      var routes = string.Join(",", Detours.Select(via => Road(points, via)));
      return Task.FromResult(
        new HttpResponseMessage(HttpStatusCode.OK)
        {
          Content = new StringContent($"{{\"routes\":[{routes}]}}"),
        }
      );
    }

    private static string Road(string[] points, RoutePoint[] via)
    {
      var bend = string.Concat(
        via.Select(point =>
          string.Create(
            CultureInfo.InvariantCulture,
            $"{{\"latitude\":{point.Latitude},"
              + $"\"longitude\":{point.Longitude}}},"
          )
        )
      );
      var legs = string.Join(
        ",",
        points
          .Zip(points.Skip(1))
          .Select(
            (leg, index) =>
              "{\"summary\":{\"lengthInMeters\":1000,"
              + "\"travelTimeInSeconds\":60},"
              + $"\"points\":[{leg.First},"
              + (index == points.Length - 2 ? bend : "")
              + $"{leg.Second}]}}"
          )
      );
      var legCount = points.Length - 1;
      var meters = (1000 * legCount).ToString(CultureInfo.InvariantCulture);
      var seconds = (60 * legCount).ToString(CultureInfo.InvariantCulture);
      return "{\"summary\":{\"lengthInMeters\":"
        + meters
        + ",\"travelTimeInSeconds\":"
        + seconds
        + "},\"legs\":["
        + legs
        + "]}";
    }
  }
}
