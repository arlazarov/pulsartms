using System.Net;
using System.Text;
using Infrastructure.Integrations.Samsara;
using Server.Tests.Support;

namespace Server.Tests.Fleet;

[Trait("Category", "Fleet")]
[Trait("Kind", "Unit")]
public sealed class OutsideTemperatureTests
{
  [Fact]
  public async Task FeedPreservesCursorParametersAndReadsLegacyDecorations()
  {
    using var handler = new Handler(_ =>
      Reply(
        """
        {"data":[{"id":"truck","gps":[{"time":"2026-09-12T15:00:00Z","latitude":40,
          "ambientAirTemperatureMilliC":{"time":"2026-09-12T14:00:00Z","value":21000}}],
          "engineStates":[{"time":"2026-09-12T15:00:00Z","value":"On",
            "ambientAirTemperatureMilliC":{"time":"2026-09-12T14:30:00Z","value":-5500}}],
          "fuelPercents":[{"time":"2026-09-12T15:00:00Z","value":64}]}],
         "pagination":{"endCursor":"next","hasNextPage":false}}
        """
      )
    );
    using var http = new HttpClient(handler);
    var feed = await Provider(http).GetFeedAsync("saved", default);
    Assert.Single(handler.Urls);
    Assert.Contains(
      "types=gps,engineStates,fuelPercents&after=saved",
      handler.Urls[0]
    );
    Assert.DoesNotContain("decorations=", handler.Urls[0]);
    var reading = Assert.Single(
      feed.Updates,
      x => x.OutsideTemperatureCelsius is not null
    );
    Assert.Equal(-5.5m, reading.OutsideTemperatureCelsius);
    Assert.Equal(
      DateTime.Parse("2026-09-12T14:30:00Z").ToUniversalTime(),
      reading.OutsideTemperatureUpdatedAt
    );
    Assert.Null(reading.Gps);
    Assert.Equal("next", feed.Cursor);
  }

  [Fact]
  public async Task SnapshotMatchesByTruckAndNeverSubstitutesCoolantOrMissingValue()
  {
    using var handler = new Handler(request =>
      request.RequestUri!.Query.Contains("types=ambientAir")
        ? Reply(
          """
          {"data":[{"id":"b","ambientAirTemperatureMilliC":{"time":"2026-09-12T14:00:00Z","value":0}},
            {"id":"a","ambientAirTemperatureMilliC":{"time":"2026-09-12T14:00:00Z","value":23500}},
            {"id":"c","ambientAirTemperatureMilliC":{"time":"2026-09-12T14:00:00Z"},
             "engineCoolantTemperatureMilliC":{"time":"2026-09-12T14:00:00Z","value":80000}}]}
          """
        )
        : Reply(
          """
          {"data":[{"id":"a","gps":{"time":"2026-09-12T15:00:00Z","latitude":40},"fuelPercent":{"value":64}},
            {"id":"b","gps":{"time":"2026-09-12T15:00:00Z","latitude":41}},
            {"id":"c","gps":{"time":"2026-09-12T15:00:00Z","latitude":42}}]}
          """
        )
    );
    using var http = new HttpClient(handler);
    var values = await Provider(http).GetVehicleTelemetryAsync();
    Assert.Equal(2, handler.Urls.Count);
    Assert.Equal(
      23.5m,
      values.Single(x => x.ExternalId == "a").OutsideTemperatureCelsius
    );
    Assert.Equal(64, values.Single(x => x.ExternalId == "a").FuelPercent);
    Assert.Equal(
      0,
      values.Single(x => x.ExternalId == "b").OutsideTemperatureCelsius
    );
    Assert.Null(
      values.Single(x => x.ExternalId == "c").OutsideTemperatureCelsius
    );
    Assert.Null(
      values.Single(x => x.ExternalId == "c").OutsideTemperatureUpdatedAt
    );
  }

  [Fact]
  public async Task OptionalTemperatureFailureDoesNotDiscardGps()
  {
    using var handler = new Handler(request =>
      request.RequestUri!.Query.Contains("types=ambientAir")
        ? new(HttpStatusCode.ServiceUnavailable)
        : Reply(
          """{"data":[{"id":"a","gps":{"time":"2026-09-12T15:00:00Z","latitude":40}}]}"""
        )
    );
    using var http = new HttpClient(handler);
    var value = Assert.Single(await Provider(http).GetVehicleTelemetryAsync());
    Assert.Equal(40, value.Latitude);
    Assert.Null(value.OutsideTemperatureCelsius);
  }

  [Fact]
  public async Task CancellationIsNotAnUnavailableReading()
  {
    using var handler = new Handler(_ =>
      throw new OperationCanceledException()
    );
    using var http = new HttpClient(handler);
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();
    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      () => Provider(http).GetVehicleTelemetryAsync(cancellation.Token)
    );
  }

  private static SamsaraFleetTelemetryProvider Provider(HttpClient http) =>
    new(
      new SamsaraApiService(
        http,
        new StubProviderCredentials(("apiKey", "test"))
      )
    );

  private static HttpResponseMessage Reply(string json) =>
    new(HttpStatusCode.OK)
    {
      Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

  private sealed class Handler(
    Func<HttpRequestMessage, HttpResponseMessage> reply
  ) : HttpMessageHandler
  {
    public List<string> Urls { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request,
      CancellationToken ct
    )
    {
      ct.ThrowIfCancellationRequested();
      Urls.Add(request.RequestUri!.PathAndQuery);
      return Task.FromResult(reply(request));
    }
  }
}
