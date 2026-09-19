using System.Net;
using System.Text.Json;
using Application.Features.Eta.Algorithms;
using Infrastructure.Integrations.Samsara;
using Microsoft.Extensions.Logging.Abstractions;

namespace Server.Tests.Fleet;

[Trait("Category", "Fleet")]
[Trait("Kind", "Integration")]
public class SamsaraHosHistoryTests
{
  [Fact]
  public async Task ActualSleeperBedPayloadProducesUsableHistoryAndReusesTheCache()
  {
    using var handler = new Handler();
    using var client = new HttpClient(handler);
    var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
    using var cache = new SamsaraHosHistoryCache(clock);
    using var catalog = new SamsaraDriverCatalogCache(clock);
    var provider = new SamsaraHosHistoryProvider(
      new(client, new StubProviderCredentials(("apiKey", "test"))),
      cache,
      catalog,
      clock,
      NullLogger<SamsaraHosHistoryProvider>.Instance
    );
    var history = (await provider.GetAsync("test", default))!;
    Assert.Contains(history.Periods, p => p.Status == "sleeperBerth");
    Assert.DoesNotContain(history.Periods, p => p.Status == "sleeperBed");
    Assert.NotNull(HosTimeline.Create(history, history.Through));
    Assert.Equal(
      300,
      HosDutyStatus.Read(history, null, history.Through)!.RestMinutes
    );
    Assert.Same(history, await provider.GetAsync("test", default));
    Assert.Equal(2, handler.Calls);
  }

  private sealed class Handler : HttpMessageHandler
  {
    public int Calls;

    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request,
      CancellationToken cancellationToken
    )
    {
      Calls++;
      if (request.RequestUri!.AbsolutePath.EndsWith("/drivers"))
        return Task.FromResult(
          new HttpResponseMessage(HttpStatusCode.OK)
          {
            Content = new StringContent(
              """{"data":[{"id":"test","timezone":"Etc/UTC"}]}"""
            ),
          }
        );
      var query = request
        .RequestUri.Query.TrimStart('?')
        .Split('&')
        .Select(x => x.Split('=', 2))
        .ToDictionary(x => x[0], x => Uri.UnescapeDataString(x[1]));
      var start = DateTimeOffset.Parse(query["startTime"]);
      var end = DateTimeOffset.Parse(query["endTime"]);
      var periods = new[]
      {
        new
        {
          logStartTime = start,
          logEndTime = end.AddHours(-5),
          hosStatusType = "onDuty",
        },
        new
        {
          logStartTime = end.AddHours(-5),
          logEndTime = end.AddHours(-4),
          hosStatusType = "offDuty",
        },
        new
        {
          logStartTime = end.AddHours(-4),
          logEndTime = end.AddHours(-3),
          hosStatusType = "personalConveyance",
        },
        new
        {
          logStartTime = end.AddHours(-3),
          logEndTime = end,
          hosStatusType = "sleeperBed",
        },
      };
      return Task.FromResult(
        new HttpResponseMessage(HttpStatusCode.OK)
        {
          Content = new StringContent(
            JsonSerializer.Serialize(
              new
              {
                data = new[]
                {
                  new { driver = new { id = "test" }, hosLogs = periods },
                },
              }
            )
          ),
        }
      );
    }
  }
}
