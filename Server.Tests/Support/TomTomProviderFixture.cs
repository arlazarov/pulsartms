using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services.Routes;
using Infrastructure.Integrations.TomTom;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Server.Tests.Support;

internal sealed class TomTomProviderFixture : IAsyncDisposable
{
  private readonly SqliteConnection connection;
  private readonly HttpClient client;
  private readonly Handler handler;
  private readonly TomTomRoutingProvider provider;
  public AppDbContext Db { get; }
  public int Calls => handler.Calls;

  private TomTomProviderFixture(SqliteConnection connection, AppDbContext db, HttpClient client,
    Handler handler, TomTomRoutingProvider provider)
  { this.connection = connection; Db = db; this.client = client; this.handler = handler; this.provider = provider; }

  public AppDbContext CreateObserver() => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection.ConnectionString).Options);
  public Task<TruckRoute> CalculateAsync(double latitude = 41, CancellationToken ct = default) =>
    provider.CalculateAsync([new(40, -80), new(latitude, -80)], new() { UsesFleetDefaults = true }, ct);
  public Task<TruckRoute> CalculateAsync(IReadOnlyList<RoutePoint> points, CancellationToken ct = default) =>
    provider.CalculateAsync(points, new() { UsesFleetDefaults = true }, ct);

  public static async Task<TomTomProviderFixture> CreateAsync(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send,
    int dailyRequestLimit = 1, TimeSpan? timeout = null)
  {
    var connection = new SqliteConnection($"Data Source=tomtom-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
    await connection.OpenAsync();
    var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
    await db.Database.EnsureCreatedAsync();
    var handler = new Handler(send);
    var client = new HttpClient(handler);
    if (timeout.HasValue) client.Timeout = timeout.Value;
    var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
      { ["TomTom:ApiKey"] = "test-only", ["TomTom:DailyRequestLimit"] = dailyRequestLimit.ToString(System.Globalization.CultureInfo.InvariantCulture) }).Build();
    return new(connection, db, client, handler, new(client, config, db,
      new RouteRequestValidator(), new UnusedAddressGeocoder(), new RouteSectionValidator()));
  }

  public async ValueTask DisposeAsync()
  {
    client.Dispose();
    await Db.DisposeAsync();
    await connection.DisposeAsync();
  }

  private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
  {
    public int Calls { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    { Calls++; return send(request, cancellationToken); }
  }
}
