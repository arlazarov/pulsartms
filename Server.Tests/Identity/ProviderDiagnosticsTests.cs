using Infrastructure.Integrations.Http;
using System.Net;

namespace Server.Tests.Identity;

[Trait("Category", "Identity")]
public class ProviderDiagnosticsTests
{
  [Fact]
  public async Task HttpFailuresRetainStatusWithoutLeakingQueryOrResponse()
  {
    using var client = new HttpClient(new Failure());
    var service = new Provider(client);
    var error = await Assert.ThrowsAsync<HttpRequestException>(() => service.GetAsync<object>("https://example.com?key=SECRET"));
    Assert.Equal(HttpStatusCode.BadGateway, error.StatusCode);
    Assert.DoesNotContain("SECRET", error.ToString());
    Assert.DoesNotContain("PRIVATE BODY", error.ToString());
  }
  private sealed class Provider(HttpClient client) : BaseApiService(client);
  private sealed class Failure : HttpMessageHandler
  {
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
      => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("PRIVATE BODY") });
  }
}
