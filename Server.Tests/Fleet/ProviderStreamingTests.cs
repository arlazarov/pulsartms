using System.Net;
using System.Text;
using Infrastructure.Integrations.Http;
using Infrastructure.Integrations.Torque;
using Microsoft.Extensions.Configuration;

namespace Server.Tests.Fleet;

[Trait("Category", "Fleet")]
[Trait("Kind", "Unit")]
public sealed class ProviderStreamingTests
{
  [Fact]
  public async Task ProviderReadsTheBodyStreamWithoutBufferingTheWholeResponse()
  {
    using var content = new ProviderStreamingContent(
      new MemoryStream(Encoding.UTF8.GetBytes("\"streamed\""))
    );
    using var client = new HttpClient(
      new Handler(() => new(HttpStatusCode.OK) { Content = content })
    );
    Assert.Equal(
      "streamed",
      await new Provider(client).GetAsync<string>("https://example.test")
    );
    Assert.Equal(1, content.StreamOpens);
  }

  [Fact]
  public async Task DeclaredOversizeFailsBeforeOpeningTheBody()
  {
    using var content = new ProviderStreamingContent(new MemoryStream());
    content.Headers.ContentLength = 8 * 1024 * 1024 + 1;
    using var client = new HttpClient(
      new Handler(() => new(HttpStatusCode.OK) { Content = content })
    );
    await Assert.ThrowsAsync<InvalidOperationException>(
      () => new Provider(client).GetAsync<string>("https://example.test")
    );
    Assert.Equal(0, content.StreamOpens);
  }

  [Fact]
  public async Task UndeclaredOversizeStopsReadingAtTheByteLimitAndDisposesTheStream()
  {
    using var stream = new EndlessJsonStringStream();
    using var content = new ProviderStreamingContent(stream);
    using var client = new HttpClient(
      new Handler(() => new(HttpStatusCode.OK) { Content = content })
    );
    await Assert.ThrowsAsync<InvalidOperationException>(
      () => new Provider(client).GetAsync<string>("https://example.test")
    );
    Assert.Equal(8 * 1024 * 1024 + 1, stream.ReadBytes);
    Assert.True(stream.Disposed);
  }

  [Fact]
  public async Task HttpTimeoutStillCoversAResponseBodyThatStopsProducingBytes()
  {
    using var content = new ProviderStreamingContent(
      new DelayedProviderStream()
    );
    using var client = new HttpClient(
      new Handler(() => new(HttpStatusCode.OK) { Content = content })
    )
    {
      Timeout = TimeSpan.FromMilliseconds(250),
    };
    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      () =>
        new Provider(client)
          .GetAsync<string>("https://example.test")
          .WaitAsync(TimeSpan.FromSeconds(5))
    );
    Assert.Equal(1, content.StreamOpens);
  }

  [Fact]
  public async Task TorqueStopsEndlessPaginationWithoutSendingAnExtraRequest()
  {
    var handler = new Handler(
      () =>
        new(HttpStatusCode.OK)
        {
          Content = new StringContent(
            """{"data":[{"loadNumber":1}],"totalCount":2147483647,"itemsPerPage":1}"""
          ),
        }
    );
    using var client = new HttpClient(handler);
    var configuration = new ConfigurationBuilder()
      .AddInMemoryCollection(
        new Dictionary<string, string?>
        {
          ["TorqueAI:BaseUrl"] = "https://example.test",
        }
      )
      .Build();
    var api = new TorqueApiService(
      client,
      configuration,
      new StubProviderCredentials(("apiKey", "test"))
    );
    await Assert.ThrowsAsync<InvalidOperationException>(
      () => api.GetDispatchesAsync(new(2026, 9, 1), new(2026, 9, 1))
    );
    Assert.Equal(256, handler.Calls);
  }

  private sealed class Provider(HttpClient client) : BaseApiService(client);

  private sealed class Handler(Func<HttpResponseMessage> response)
    : HttpMessageHandler
  {
    public int Calls;

    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request,
      CancellationToken ct
    )
    {
      Calls++;
      return Task.FromResult(response());
    }
  }
}
