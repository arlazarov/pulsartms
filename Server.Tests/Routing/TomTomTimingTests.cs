using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Models;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Integration")]
public sealed class TomTomTimingTests
{
  private static readonly RoutePoint[] Points = Enumerable
    .Range(0, 6)
    .Select(i => new RoutePoint(40 + i * .1, -80))
    .ToArray();

  [Fact]
  public async Task FreshAndPreviouslyCachedProviderRoundingUsesCanonicalLegTimingWithoutAnotherRequest()
  {
    await using var fixture = await TomTomProviderFixture.CreateAsync(
      (_, _) => Task.FromResult(Response(79392))
    );
    var fresh = await fixture.CalculateAsync(Points);
    Assert.Equal(79393, fresh.Seconds);
    Assert.Equal(fresh.Legs.Sum(leg => leg.Seconds), fresh.Seconds);
    Assert.Equal(200, fresh.Miles, 8);
    var row = await fixture.Db.RoutingApiCalls.SingleAsync();
    var stored = JsonNode.Parse(row.ResultJson!)!;
    Assert.Equal(79393, stored["seconds"]!.GetValue<double>());
    stored["seconds"] = 79392;
    row.ResultJson = stored.ToJsonString();
    await fixture.Db.SaveChangesAsync();

    await using var transaction =
      await fixture.Db.Database.BeginTransactionAsync();
    var cached = await fixture.CalculateAsync(Points);
    Assert.Equal(79393, cached.Seconds);
    Assert.Equal(fresh.Legs, cached.Legs, new LegComparer());
    Assert.Equal(1, fixture.Calls);
    Assert.Equal(1, await fixture.Db.RoutingApiCalls.CountAsync());
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task TimingBeyondIndependentWholeSecondRoundingFailsClosed(
    bool cached
  )
  {
    await using var fixture = await TomTomProviderFixture.CreateAsync(
      (_, _) => Task.FromResult(Response(cached ? 79392 : 79380))
    );
    if (cached)
    {
      await fixture.CalculateAsync(Points);
      var row = await fixture.Db.RoutingApiCalls.SingleAsync();
      var stored = JsonNode.Parse(row.ResultJson!)!;
      stored["seconds"] = 79380;
      row.ResultJson = stored.ToJsonString();
      await fixture.Db.SaveChangesAsync();
    }
    var failure = await Assert.ThrowsAsync<RoutePlanningException>(
      () => fixture.CalculateAsync(Points)
    );
    Assert.Contains("inconsistent route timing", failure.Message);
    await Assert.ThrowsAsync<RoutePlanningException>(
      () => fixture.CalculateAsync(Points)
    );
    Assert.Equal(1, fixture.Calls);
  }

  private static HttpResponseMessage Response(double seconds) =>
    new(HttpStatusCode.OK)
    {
      Content = new StringContent(
        JsonSerializer.Serialize(
          new
          {
            routes = new[]
            {
              new
              {
                summary = new
                {
                  lengthInMeters = 200 * 1609.344,
                  travelTimeInSeconds = seconds,
                },
                legs = new[] { 10000, 15000, 20000, 16000, 18393 }.Select(
                  (duration, i) =>
                    new
                    {
                      summary = new
                      {
                        lengthInMeters = 40 * 1609.344,
                        travelTimeInSeconds = duration,
                      },
                      points = new[] { Points[i], Points[i + 1] }.Select(
                        point => new
                        {
                          latitude = point.Latitude,
                          longitude = point.Longitude,
                        }
                      ),
                    }
                ),
              },
            },
          }
        )
      ),
    };

  private sealed class LegComparer : IEqualityComparer<RouteLeg>
  {
    public bool Equals(RouteLeg? first, RouteLeg? second) =>
      first is not null
      && second is not null
      && first.Miles == second.Miles
      && first.Seconds == second.Seconds
      && first.Points.SequenceEqual(second.Points);

    public int GetHashCode(RouteLeg leg) =>
      HashCode.Combine(leg.Miles, leg.Seconds);
  }
}
