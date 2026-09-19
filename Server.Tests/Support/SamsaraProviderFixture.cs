using System.Net;
using System.Net.Http.Json;
using Infrastructure.Integrations.Samsara;
using Microsoft.Extensions.Logging.Abstractions;

namespace Server.Tests.Support;

public sealed class SamsaraProviderFixture : IDisposable
{
  public sealed record HistoryRequest(
    string Driver,
    DateTimeOffset From,
    DateTimeOffset To
  );

  private readonly Handler handler;
  private readonly HttpClient http;
  public ManualTimeProvider Clock { get; } = new();
  public SamsaraHosHistoryCache History { get; }
  public SamsaraDriverCatalogCache Catalog { get; }
  public SamsaraApiService Api { get; }
  public List<HistoryRequest> Requests { get; } = [];
  public string[] Drivers { get; set; } = ["driver"];
  public int CatalogCalls { get; private set; }
  public HttpStatusCode CatalogStatus { get; set; } = HttpStatusCode.OK;
  public Func<
    HistoryRequest,
    CancellationToken,
    Task<HttpResponseMessage>
  > Read { get; set; } = (request, _) => Task.FromResult(Reply(request));

  public SamsaraProviderFixture()
  {
    History = new(Clock);
    Catalog = new(Clock);
    handler = new(this);
    http = new(handler);
    Api = new(http, new StubProviderCredentials(("apiKey", "test")));
  }

  public SamsaraHosHistoryProvider Provider() =>
    new(
      Api,
      History,
      Catalog,
      Clock,
      NullLogger<SamsaraHosHistoryProvider>.Instance
    );

  public static HttpResponseMessage Reply(HistoryRequest request) =>
    new(HttpStatusCode.OK)
    {
      Content = JsonContent.Create(
        new
        {
          data = new[]
          {
            new
            {
              driver = new { id = request.Driver },
              hosLogs = new[]
              {
                new
                {
                  logStartTime = request.From,
                  logEndTime = request.To,
                  hosStatusType = "onDuty",
                },
              },
            },
          },
        }
      ),
    };

  private sealed class Handler(SamsaraProviderFixture fixture)
    : HttpMessageHandler
  {
    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request,
      CancellationToken ct
    )
    {
      if (
        request.RequestUri!.AbsolutePath.EndsWith(
          "/drivers",
          StringComparison.Ordinal
        )
      )
      {
        fixture.CatalogCalls++;
        return Task.FromResult(
          new HttpResponseMessage(fixture.CatalogStatus)
          {
            Content = JsonContent.Create(
              new
              {
                data = fixture.Drivers.Select(id => new
                {
                  id,
                  timezone = "Etc/UTC",
                }),
              }
            ),
          }
        );
      }
      var query = request
        .RequestUri.Query.TrimStart('?')
        .Split('&')
        .Select(x => x.Split('=', 2))
        .ToDictionary(x => x[0], x => Uri.UnescapeDataString(x[1]));
      var value = new HistoryRequest(
        query["driverIds"],
        DateTimeOffset.Parse(query["startTime"]),
        DateTimeOffset.Parse(query["endTime"])
      );
      fixture.Requests.Add(value);
      return fixture.Read(value, ct);
    }
  }

  public void Dispose()
  {
    http.Dispose();
    handler.Dispose();
    History.Dispose();
    Catalog.Dispose();
  }
}
