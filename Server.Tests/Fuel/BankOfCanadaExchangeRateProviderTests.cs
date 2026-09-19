using System.Net;
using Infrastructure.Integrations.BankOfCanada;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Unit")]
public sealed class BankOfCanadaExchangeRateProviderTests
{
  [Fact]
  public async Task FixedOfficialSeriesIsInvertedWithDecimalPrecision()
  {
    var handler = new Handler(new StringContent(Payload()));
    using var client = new HttpClient(handler);
    var clock = new ManualTimeProvider(
      new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero)
    );
    var provider = new BankOfCanadaExchangeRateProvider(client, clock);

    var rate = await provider.ReadAsync(default);

    Assert.Equal(1m / 1.3917m, rate.UsdPerCad);
    Assert.Equal(new DateOnly(2026, 9, 15), rate.ObservedOn);
    Assert.Equal(clock.GetUtcNow().UtcDateTime, rate.RetrievedAt);
    Assert.Equal(
      "https://www.bankofcanada.ca/valet/observations/FXUSDCAD/json?recent=1",
      handler.Uri?.AbsoluteUri
    );
    Assert.Equal(HttpMethod.Get, handler.Method);
    Assert.Equal(1, handler.Calls);
  }

  [Theory]
  [InlineData("2026-09-11")]
  [InlineData("2026-08-01")]
  public async Task HistoricalDateIsRetainedForApplicationFreshnessPolicy(
    string date
  )
  {
    using var client = new HttpClient(
      new Handler(new StringContent(Payload(date: date)))
    );
    var provider = new BankOfCanadaExchangeRateProvider(
      client,
      new ManualTimeProvider(new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero))
    );

    var rate = await provider.ReadAsync(default);

    Assert.Equal(DateOnly.ParseExact(date, "yyyy-MM-dd"), rate.ObservedOn);
  }

  [Theory]
  [InlineData("0")]
  [InlineData("-1")]
  [InlineData("1,3917")]
  [InlineData("NaN")]
  [InlineData("Infinity")]
  [InlineData("0.0000000000000000000000000001")]
  [InlineData("0.49")]
  [InlineData("10.1")]
  [InlineData("79228162514264337593543950336")]
  public async Task InvalidOrUnsupportedRatesNeverBecomeAFallback(string value)
  {
    using var client = new HttpClient(
      new Handler(new StringContent(Payload(value)))
    );
    var provider = new BankOfCanadaExchangeRateProvider(
      client,
      new ManualTimeProvider(new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero))
    );

    await Assert.ThrowsAsync<InvalidOperationException>(
      () => provider.ReadAsync(default)
    );
  }

  [Theory]
  [InlineData("2026-09-17")]
  [InlineData("2026-02-30")]
  [InlineData("09/15/2026")]
  [InlineData("0001-01-01")]
  public async Task InvalidAndFutureObservationDatesAreRejected(string date)
  {
    using var client = new HttpClient(
      new Handler(new StringContent(Payload(date: date)))
    );
    var provider = new BankOfCanadaExchangeRateProvider(
      client,
      new ManualTimeProvider(new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero))
    );

    await Assert.ThrowsAsync<InvalidOperationException>(
      () => provider.ReadAsync(default)
    );
  }

  [Theory]
  [InlineData("{}")]
  [InlineData("null")]
  [InlineData("{\"observations\":[]}")]
  [InlineData("{\"observations\":[null]}")]
  [InlineData("{\"observations\":[{},{}]}")]
  [InlineData("{\"observations\":[{\"d\":\"2026-09-15\"}]}")]
  [InlineData("private-provider-body")]
  public async Task InvalidPayloadFailsWithoutExposingProviderContent(
    string payload
  )
  {
    using var client = new HttpClient(new Handler(new StringContent(payload)));
    var provider = new BankOfCanadaExchangeRateProvider(
      client,
      new ManualTimeProvider()
    );

    var error = await Assert.ThrowsAsync<InvalidOperationException>(
      () => provider.ReadAsync(default)
    );

    Assert.Equal(
      "The official exchange-rate observation is invalid.",
      error.Message
    );
    Assert.Null(error.InnerException);
  }

  [Fact]
  public async Task FailedHttpStatusDoesNotExposeTheResponseBody()
  {
    using var client = new HttpClient(
      new Handler(
        new StringContent("private-provider-body"),
        HttpStatusCode.TooManyRequests
      )
    );
    var provider = new BankOfCanadaExchangeRateProvider(
      client,
      new ManualTimeProvider()
    );

    var error = await Assert.ThrowsAsync<HttpRequestException>(
      () => provider.ReadAsync(default)
    );

    Assert.Equal(HttpStatusCode.TooManyRequests, error.StatusCode);
    Assert.Equal("Upstream API request failed.", error.Message);
  }

  [Fact]
  public async Task DeclaredOversizeFailsBeforeOpeningTheStream()
  {
    using var content = new ProviderStreamingContent(new MemoryStream());
    content.Headers.ContentLength = 8 * 1024 * 1024 + 1;
    using var client = new HttpClient(new Handler(content));
    var provider = new BankOfCanadaExchangeRateProvider(
      client,
      new ManualTimeProvider()
    );

    await Assert.ThrowsAsync<InvalidOperationException>(
      () => provider.ReadAsync(default)
    );

    Assert.Equal(0, content.StreamOpens);
  }

  [Fact]
  public async Task UndeclaredOversizeStopsAtTheSharedProviderLimit()
  {
    using var stream = new EndlessJsonStringStream();
    using var content = new ProviderStreamingContent(stream);
    using var client = new HttpClient(new Handler(content));
    var provider = new BankOfCanadaExchangeRateProvider(
      client,
      new ManualTimeProvider()
    );

    await Assert.ThrowsAsync<InvalidOperationException>(
      () => provider.ReadAsync(default)
    );

    Assert.Equal(8 * 1024 * 1024 + 1, stream.ReadBytes);
    Assert.True(stream.Disposed);
  }

  [Fact]
  public async Task TimeoutCoversTheResponseBodyNotOnlyHeaders()
  {
    using var content = new ProviderStreamingContent(
      new DelayedProviderStream()
    );
    using var client = new HttpClient(new Handler(content))
    {
      Timeout = TimeSpan.FromMilliseconds(250),
    };
    var provider = new BankOfCanadaExchangeRateProvider(
      client,
      new ManualTimeProvider()
    );

    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      () => provider.ReadAsync(default).WaitAsync(TimeSpan.FromSeconds(5))
    );
  }

  private static string Payload(
    string rate = "1.3917",
    string date = "2026-09-15"
  ) =>
    $$$"""
      {"observations":[{"d":"{{{date}}}","FXUSDCAD":{"v":"{{{rate}}}"}}]}
      """;

  private sealed class Handler(
    HttpContent content,
    HttpStatusCode status = HttpStatusCode.OK
  ) : HttpMessageHandler
  {
    public Uri? Uri { get; private set; }
    public HttpMethod? Method { get; private set; }
    public int Calls { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request,
      CancellationToken ct
    )
    {
      ct.ThrowIfCancellationRequested();
      Uri = request.RequestUri;
      Method = request.Method;
      Calls++;
      return Task.FromResult(
        new HttpResponseMessage(status) { Content = content }
      );
    }
  }
}
