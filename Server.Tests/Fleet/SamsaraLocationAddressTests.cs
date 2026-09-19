using System.Net;
using System.Text;
using Infrastructure.Integrations.Samsara;
using Server.Tests.Support;

namespace Server.Tests.Fleet;

[Trait("Category", "Fleet")]
[Trait("Kind", "Unit")]
public sealed class SamsaraLocationAddressTests
{
  [Theory]
  [InlineData(
    """{"streetNumber":" 74 ","street":"Briar Road","city":"Bedford","state":"NH","postalCode":"03110","country":"US"}""",
    "74 Briar Road, Bedford, NH 03110, US"
  )]
  [InlineData(
    """{"street":"I 40","city":"Los Pinos","state":"NM","country":"US"}""",
    "I 40, Los Pinos, NM, US"
  )]
  [InlineData(
    """{"streetNumber":"100","street":"King St","city":"Toronto","state":"ON","postalCode":"M5H 1J9","country":"CA"}""",
    "100 King St, Toronto, ON M5H 1J9, CA"
  )]
  [InlineData(
    """{"street":"I 40","city":"Los Pinos","state":"NM","postalCode":null,"country":null}""",
    "I 40, Los Pinos, NM"
  )]
  [InlineData("{}", "")]
  [InlineData("null", "")]
  public async Task StructuredAddressKeepsProviderZipAndCountryWithoutGuessing(
    string address,
    string expected
  )
  {
    using var handler = new Handler(
      $$$"""
      {"data":[{"asset":{"id":"truck"},"happenedAtTime":"2026-09-12T19:00:00Z",
        "location":{"latitude":40,"longitude":-80,"headingDegrees":90,"address":{{{address}}}},
        "speed":{"gpsSpeedMetersPerSecond":10}}],"pagination":{"endCursor":"next","hasNextPage":false}}
      """
    );
    using var http = new HttpClient(handler);
    var provider = new SamsaraFleetTelemetryProvider(
      new(http, new StubProviderCredentials(("apiKey", "test")))
    );
    var result = await provider.GetLocationStreamAsync(
      ["truck"],
      DateTime.UtcNow.AddMinutes(-1),
      DateTime.UtcNow
    );
    var point = Assert.Single(result.Data);
    Assert.Equal(expected, point.FormattedLocation);
    Assert.Equal("truck", point.ExternalId);
    Assert.Equal(40, point.Latitude);
    Assert.Equal(22.36936m, point.Speed);
    Assert.Equal(
      DateTime.Parse("2026-09-12T19:00:00Z").ToUniversalTime(),
      point.UpdatedAt
    );
    Assert.Single(handler.Requests);
    Assert.Contains("includeReverseGeo=true", handler.Requests[0]);
  }

  [Theory]
  [InlineData("null")]
  [InlineData("{}")]
  public async Task LegacyFormattedAddressRemainsSupported(string nested)
  {
    using var handler = new Handler(
      $$$"""
      {"data":[{"asset":{"id":"truck"},"location":{"address":{{{nested}}}},
        "address":{"formattedAddress":"  I 40, Los Pinos, NM 87026, US  "}}],"pagination":{}}
      """
    );
    using var http = new HttpClient(handler);
    var provider = new SamsaraFleetTelemetryProvider(
      new(http, new StubProviderCredentials(("apiKey", "test")))
    );
    var result = await provider.GetLocationStreamAsync(
      ["truck"],
      DateTime.UtcNow.AddMinutes(-1),
      DateTime.UtcNow
    );
    Assert.Equal(
      "I 40, Los Pinos, NM 87026, US",
      Assert.Single(result.Data).FormattedLocation
    );
    Assert.Single(handler.Requests);
  }

  private sealed class Handler(string json) : HttpMessageHandler
  {
    public List<string> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request,
      CancellationToken ct
    )
    {
      ct.ThrowIfCancellationRequested();
      Requests.Add(request.RequestUri!.PathAndQuery);
      return Task.FromResult(
        new HttpResponseMessage(HttpStatusCode.OK)
        {
          Content = new StringContent(json, Encoding.UTF8, "application/json"),
        }
      );
    }
  }
}
