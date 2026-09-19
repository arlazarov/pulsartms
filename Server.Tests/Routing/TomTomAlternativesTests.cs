using System.Net;
using System.Text.Json;
using Application.Features.Routing.Exceptions;
using Microsoft.EntityFrameworkCore;
using Server.Tests.Support;

namespace Server.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Integration")]
public sealed class TomTomAlternativesTests
{
  [Fact]
  public async Task AlternativesKeepTruckRestrictionsAndReuseBoundedCache()
  {
    string? query = null;
    await using var f = await TomTomProviderFixture.CreateAsync(
      (request, _) =>
      {
        query = request.RequestUri!.Query;
        var body = new
        {
          routes = Enumerable
            .Range(1, 3)
            .Select(n => new
            {
              summary = new
              {
                lengthInMeters = n * 10000,
                travelTimeInSeconds = n * 600,
              },
              legs = new[]
              {
                new
                {
                  summary = new
                  {
                    lengthInMeters = n * 10000,
                    travelTimeInSeconds = n * 600,
                  },
                  points = new[]
                  {
                    new { latitude = 40, longitude = -80 },
                    new { latitude = 41, longitude = -80 },
                  },
                },
              },
            }),
        };
        return Task.FromResult(
          new HttpResponseMessage(HttpStatusCode.OK)
          {
            Content = new StringContent(JsonSerializer.Serialize(body)),
          }
        );
      }
    );
    var routes = await f.AlternativesAsync();
    Assert.Equal(3, routes.Count);
    Assert.Contains("maxAlternatives=2", query);
    Assert.Contains("travelMode=truck", query);
    Assert.Contains("vehicleCommercial=true", query);
    Assert.Contains("vehicleHeight=", query);
    Assert.Contains("vehicleWeight=", query);
    Assert.Contains("USHazmatClass3", query);
    var cached = await f.AlternativesAsync();
    Assert.Equal(routes.Select(r => r.Miles), cached.Select(r => r.Miles));
    Assert.All(cached, r => Assert.Equal(2, r.Points.Count));
    Assert.Equal(1, f.Calls);
    Assert.Equal(
      "alternatives",
      (await f.Db.RoutingApiCalls.AsNoTracking().SingleAsync()).Operation
    );
  }

  [Theory]
  [InlineData("{}")]
  [InlineData("{\"routes\":null}")]
  [InlineData("{\"routes\":[]}")]
  [InlineData("[]")]
  public async Task MalformedAlternativesLeaveControlledCooldown(string body)
  {
    await using var f = await TomTomProviderFixture.CreateAsync(
      (_, _) =>
        Task.FromResult(
          new HttpResponseMessage(HttpStatusCode.OK)
          {
            Content = new StringContent(body),
          }
        )
    );
    await Assert.ThrowsAsync<RoutePlanningException>(
      () => f.AlternativesAsync()
    );
    await Assert.ThrowsAsync<RoutePlanningException>(
      () => f.AlternativesAsync()
    );
    Assert.Equal(1, f.Calls);
    Assert.Null(
      (await f.Db.RoutingApiCalls.AsNoTracking().SingleAsync()).ResultJson
    );
  }
}
