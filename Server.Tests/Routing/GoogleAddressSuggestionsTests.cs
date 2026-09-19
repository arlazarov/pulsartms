using System.Net;
using System.Text.Json;
using Infrastructure.Integrations.Google.Places;
using Microsoft.Extensions.Configuration;

namespace Server.Tests.Routing;

[Trait("Category", "Addresses")]
[Trait("Kind", "Unit")]
public sealed class GoogleAddressSuggestionsTests
{
  [Fact]
  public async Task UsesSessionForPredictionsAndStructuredDetails()
  {
    var session = Guid.NewGuid();
    var calls = 0;
    using var http = new HttpClient(
      new Stub(async request =>
      {
        calls++;
        Assert.Equal(
          "test-key",
          Assert.Single(request.Headers.GetValues("X-Goog-Api-Key"))
        );
        if (calls == 1)
        {
          using var body = JsonDocument.Parse(
            await request.Content!.ReadAsStringAsync()
          );
          Assert.Equal(
            session.ToString(),
            body.RootElement.GetProperty("sessionToken").GetString()
          );
          Assert.Equal(
            "123 Main",
            body.RootElement.GetProperty("input").GetString()
          );
          return Json(
            """
            {"suggestions":[{"placePrediction":{"placeId":"place-1",
            "text":{"text":"123 Main Street, Toronto, ON, Canada"}}}]}
            """
          );
        }
        Assert.Contains(
          "places/place-1?languageCode=en",
          request.RequestUri!.ToString()
        );
        if (calls == 2)
          Assert.DoesNotContain("sessionToken", request.RequestUri!.ToString());
        else
          Assert.Contains(
            "sessionToken=" + session,
            request.RequestUri!.ToString()
          );
        Assert.Equal(
          "addressComponents",
          Assert.Single(request.Headers.GetValues("X-Goog-FieldMask"))
        );
        return Json(
          """
          {"addressComponents":[
            {"longText":"123","types":["street_number"]},
            {"longText":"Main Street","types":["route"]},
            {"longText":"Toronto","types":["locality"]},
            {"shortText":"ON","types":["administrative_area_level_1"]},
            {"shortText":"CA","types":["country"]},
            {"longText":"M1B 1B1","types":["postal_code"]}
          ]}
          """
        );
      })
    );
    var provider = new GoogleAddressSuggestions(http, Config());
    var suggestions = await provider.SuggestAsync("123 Main", session, default);
    Assert.Equal("place-1", Assert.Single(suggestions.Items).Id);
    Assert.Null(suggestions.Items[0].SavedAddress);
    Assert.Equal("M1B 1B1", suggestions.Items[0].PostalCode);
    var resolved = await provider.ResolveAsync("place-1", session, default);
    Assert.Equal("123 Main Street", resolved!.Address);
    Assert.Equal("ON", resolved.Region);
    Assert.Equal("CA", resolved.Country);
    Assert.Equal("M1B 1B1", resolved.PostalCode);
  }

  [Fact]
  public async Task ProviderFailureIsReportedWithoutLosingHistoryContract()
  {
    using var http = new HttpClient(
      new Stub(_ =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden))
      )
    );
    var provider = new GoogleAddressSuggestions(http, Config());
    var result = await provider.SuggestAsync("Main", Guid.NewGuid(), default);
    Assert.True(result.ProviderUnavailable);
    Assert.Empty(result.Items);
  }

  private static IConfiguration Config() =>
    new ConfigurationBuilder()
      .AddInMemoryCollection(
        new Dictionary<string, string?> { ["GooglePlaces:ApiKey"] = "test-key" }
      )
      .Build();

  private static HttpResponseMessage Json(string body) =>
    new(HttpStatusCode.OK) { Content = new StringContent(body) };

  private sealed class Stub(
    Func<HttpRequestMessage, Task<HttpResponseMessage>> send
  ) : HttpMessageHandler
  {
    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request,
      CancellationToken ct
    ) => send(request);
  }
}
