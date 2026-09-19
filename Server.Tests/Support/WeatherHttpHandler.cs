using System.Net;
using System.Net.Http.Json;

namespace Server.Tests.Support;

internal sealed class WeatherHttpHandler(object response) : HttpMessageHandler
{
  public int Calls { get; private set; }
  public Uri? Uri { get; private set; }
  public string? Key { get; private set; }

  protected override Task<HttpResponseMessage> SendAsync(
    HttpRequestMessage request,
    CancellationToken ct
  )
  {
    Calls++;
    Uri = request.RequestUri;
    Key = request.Headers.GetValues("X-Goog-Api-Key").Single();
    return Task.FromResult(
      new HttpResponseMessage(HttpStatusCode.OK)
      {
        Content = JsonContent.Create(response),
      }
    );
  }
}
