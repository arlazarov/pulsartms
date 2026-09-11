using Application.Features.Routing.Services.Routes;
using System.Net;
using Application.Features.Routing.Exceptions;
using Infrastructure.Integrations.TomTom;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Server.Tests.Routing;

[Trait("Category", "Routing")]
public class TomTomCacheTests
{
  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task RepeatedRequestsUseCacheWithoutAnExtraTransactionOrPaidCall(bool success)
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
    await db.Database.EnsureCreatedAsync();
    using var handler = new Handler(success);
    using var client = new HttpClient(handler);
    var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
      { ["TomTom:ApiKey"] = "test-only", ["TomTom:DailyRequestLimit"] = "1" }).Build();
    var provider = new TomTomRoutingProvider(client, config, db, new Application.Features.Routing.Services.Routes.RouteRequestValidator(), new UnusedAddressGeocoder(), new Application.Features.Routing.Algorithms.RouteSectionValidator());
    Task<Application.Features.Routing.Models.TruckRoute> Calculate() => provider.CalculateAsync(
      [new(40, -80), new(41, -80)], new Application.Features.Routing.Models.TruckRouteProfile {
        Confirmed = true, HeightFeet = 13.5, WidthFeet = 8.5, LengthFeet = 70, WeightPounds = 80000, Axles = 5, AxleWeightPounds = 17000
      }, default);
    if (success) Assert.Equal(40, (await Calculate()).Points[0].Latitude);
    else await Assert.ThrowsAsync<RoutePlanningException>(() => Calculate());
    await using var transaction = await db.Database.BeginTransactionAsync();
    if (success) Assert.Equal(40, (await Calculate()).Points[0].Latitude);
    else await Assert.ThrowsAsync<RoutePlanningException>(() => Calculate());
    Assert.Equal(1, handler.Calls);
    Assert.Equal(1, await db.RoutingApiCalls.CountAsync());
  }

  [Theory]
  [InlineData(400, true)]
  [InlineData(503, false)]
  [InlineData(429, false)]
  public async Task FailurePersistsReasonAndRetryPolicy(int status, bool permanent)
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
    await db.Database.EnsureCreatedAsync();
    using var handler = new Handler(false, (HttpStatusCode)status);
    using var client = new HttpClient(handler);
    var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
      { ["TomTom:ApiKey"] = "test-only" }).Build();
    var provider = new TomTomRoutingProvider(client, config, db, new Application.Features.Routing.Services.Routes.RouteRequestValidator(), new UnusedAddressGeocoder(), new Application.Features.Routing.Algorithms.RouteSectionValidator());
    var profile = new Application.Features.Routing.Models.TruckRouteProfile { UsesFleetDefaults = true };
    var error = await Assert.ThrowsAsync<RoutePlanningException>(() => provider.CalculateAsync([new(40, -80), new(41, -80)], profile, default));
    var saved = await db.RoutingApiCalls.SingleAsync();
    Assert.Equal(error.Message, saved.ErrorMessage);
    Assert.Equal(permanent, saved.ExpiresAt == DateTime.MaxValue);
    Assert.True(saved.ExpiresAt > DateTime.UtcNow.AddMinutes(3));
    await Assert.ThrowsAsync<RoutePlanningException>(() => provider.CalculateAsync([new(40, -80), new(41, -80)], profile, default));
    Assert.Equal(1, handler.Calls);
    await Assert.ThrowsAsync<RoutePlanningException>(() => provider.CalculateAsync([new(40, -80), new(42, -80)], profile, default));
    Assert.Equal(2, handler.Calls);
    config["TomTom:DailyRequestLimit"] = "2";
    var limited = await Assert.ThrowsAsync<RoutePlanningException>(() => provider.CalculateAsync([new(40, -80), new(43, -80)], profile, default));
    Assert.Equal(DateTime.UtcNow.Date.AddDays(1), limited.RetryAfter);
    Assert.Equal(2, handler.Calls);
    Assert.Equal(2, await db.RoutingApiCalls.CountAsync());
  }

  private sealed class Handler(bool success, HttpStatusCode failureStatus = HttpStatusCode.ServiceUnavailable) : HttpMessageHandler
  {
    public int Calls;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
      Calls++;
      return Task.FromResult(new HttpResponseMessage(success ? HttpStatusCode.OK : failureStatus)
      {
        Content = new StringContent("""{"routes":[{"summary":{"lengthInMeters":1000,"travelTimeInSeconds":60},"legs":[{"summary":{"lengthInMeters":1000,"travelTimeInSeconds":60},"points":[{"latitude":40,"longitude":-80},{"latitude":41,"longitude":-80}]}]}]}""")
      });
    }
  }
}
