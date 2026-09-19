using System.Net;
using Infrastructure.Integrations.Samsara;

namespace Server.Tests.Fleet;

[Trait("Category", "Fleet")]
[Trait("Kind", "Integration")]
public sealed class SamsaraOdometerTests
{
  [Fact]
  public async Task OdometerUsesItsOwnFeedAndPreservesRawSamples()
  {
    using var handler = new Handler();
    using var client = new HttpClient(handler);
    var provider = new SamsaraOdometerProvider(
      new SamsaraApiService(
        client,
        new StubProviderCredentials(("apiKey", "test"))
      )
    );
    var page = await provider.ReadAsync("odometer cursor", default);
    Assert.Equal(
      "?types=obdOdometerMeters&after=odometer%20cursor",
      handler.Query
    );
    Assert.Equal("next", page.Cursor);
    Assert.True(page.HasMore);
    var sample = Assert.Single(page.Samples);
    Assert.Equal("truck", sample.ExternalTruckId);
    Assert.Equal(123456.7m, sample.Meters);
    Assert.Equal(
      DateTimeOffset.Parse("2026-09-13T12:30:00Z"),
      sample.ObservedAt
    );
  }

  [Fact]
  public async Task MissingCursorCannotAdvanceMileageCheckpoint()
  {
    using var handler = new Handler { MissingCursor = true };
    using var client = new HttpClient(handler);
    var provider = new SamsaraOdometerProvider(
      new SamsaraApiService(
        client,
        new StubProviderCredentials(("apiKey", "test"))
      )
    );
    await Assert.ThrowsAsync<InvalidOperationException>(
      () => provider.ReadAsync(null, default)
    );
  }

  private sealed class Handler : HttpMessageHandler
  {
    public string? Query { get; private set; }
    public bool MissingCursor { get; init; }

    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request,
      CancellationToken cancellationToken
    )
    {
      Query = request.RequestUri!.Query;
      var body = MissingCursor
        ? "{\"data\":[]}"
        : """
          {
            "data":[{
              "id":"truck",
              "obdOdometerMeters":[{
                "time":"2026-09-13T12:30:00Z","value":123456.7
              }]
            }],
            "pagination":{"endCursor":"next","hasNextPage":true}
          }
          """;
      return Task.FromResult(
        new HttpResponseMessage(HttpStatusCode.OK)
        {
          Content = new StringContent(body),
        }
      );
    }
  }
}
