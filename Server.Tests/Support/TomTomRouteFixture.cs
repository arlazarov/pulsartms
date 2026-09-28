using System.Globalization;
using System.Net;
using Application.Diagnostics;
using Application.Features.Routing.Services.Routes;
using Domain.Models.Routing;
using Domain.Rules.Routing;
using Infrastructure.Integrations.GeoTimeZone;
using Infrastructure.Integrations.TomTom;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Server.Tests.Support;

// TomTomRoutingProvider over a recording fake of TomTom's route API and an
// in-memory cache database, for tests of what the provider asks for and
// what it does with the answer.
internal sealed class TomTomRouteFixture : IAsyncDisposable
{
  private readonly SqliteConnection _connection;
  private readonly AppDbContext _db;
  private readonly HttpClient _client;

  private TomTomRouteFixture(SqliteConnection connection, AppDbContext db)
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
      new RouteRegionLookup(),
      new StageTimings()
    );
  }

  public RecordingTomTom Handler { get; } = new();
  public TomTomRoutingProvider Provider { get; }

  public static async Task<TomTomRouteFixture> CreateAsync()
  {
    var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
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
// the request asked to avoid; a road whose index is in Ferries reports a
// ferry section, whatever the request asked to avoid.
internal sealed class RecordingTomTom : HttpMessageHandler
{
  public List<string> Queries { get; } = [];
  public RoutePoint[][] Detours { get; set; } =
    [
      [],
    ];
  public HashSet<int> Ferries { get; } = [];

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
    var routes = string.Join(
      ",",
      Detours.Select((via, index) => Road(points, via, Ferries.Contains(index)))
    );
    return Task.FromResult(
      new HttpResponseMessage(HttpStatusCode.OK)
      {
        Content = new StringContent($"{{\"routes\":[{routes}]}}"),
      }
    );
  }

  private static string Road(string[] points, RoutePoint[] via, bool ferry)
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
      + "]"
      + (
        ferry
          ? ",\"sections\":[{\"sectionType\":\"FERRY\","
            + "\"startPointIndex\":1,\"endPointIndex\":2}]"
          : ""
      )
      + "}";
  }
}
