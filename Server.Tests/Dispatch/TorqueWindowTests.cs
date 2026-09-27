using System.Net;
using System.Text.Json;
using Infrastructure.Integrations.Torque;
using Infrastructure.Integrations.Torque.Models;
using Microsoft.Extensions.Configuration;
using Server.Tests.Support;

namespace Server.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Integration")]
public class TorqueWindowTests
{
  [Theory]
  [InlineData("null", "")]
  [InlineData("-10", "-10")]
  [InlineData("\"35\"", "35")]
  [InlineData("\"\"", "")]
  public void ParsesBothNumericAndTextTemperatures(
    string value,
    string expected
  )
  {
    var stop = JsonSerializer.Deserialize<TorqueDispatchStopDto>(
      "{\"temperature\":" + value + "}",
      new JsonSerializerOptions(JsonSerializerDefaults.Web)
    );
    Assert.Equal(expected, stop!.Temperature);
  }

  [Fact]
  public void ParsesNumericStopNumber()
  {
    var stop = JsonSerializer.Deserialize<TorqueDispatchStopDto>(
      "{\"stopNo\":1358}",
      new JsonSerializerOptions(JsonSerializerDefaults.Web)
    );
    Assert.Equal("1358", stop!.StopNo);
  }

  [Fact]
  public async Task SplitsOlderOrderSearchIntoAcceptedWindowsAndDeduplicatesLoads()
  {
    var handler = new Handler();
    var config = new ConfigurationBuilder()
      .AddInMemoryCollection(
        new Dictionary<string, string?>
        {
          ["TorqueAI:BaseUrl"] = "https://example.test",
        }
      )
      .Build();
    var service = new TorqueApiService(
      new HttpClient(handler),
      config,
      new StubProviderCredentials(("apiKey", "test"))
    );
    var result = await service.GetDispatchesAsync(
      new(2026, 8, 7),
      new(2026, 9, 13)
    );
    Assert.Equal(2, handler.Urls.Count);
    Assert.Contains("from=2026-08-07&to=2026-09-05", handler.Urls[0]);
    Assert.Contains("from=2026-09-06&to=2026-09-13", handler.Urls[1]);
    Assert.Equal(1358, Assert.Single(result).LoadNumber);
  }

  [Fact]
  public async Task OldCreationWindowKeepsUpcomingDeliveryButSkipsOldHistory()
  {
    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    var handler = new Handler
    {
      Body = JsonSerializer.Serialize(
        new
        {
          data = new[]
          {
            new
            {
              loadNumber = 1358,
              deliveryDate = today.AddDays(2).ToString("yyyy-MM-dd"),
            },
            new
            {
              loadNumber = 1300,
              deliveryDate = today.AddDays(-20).ToString("yyyy-MM-dd"),
            },
          },
          totalCount = 2,
          itemsPerPage = 500,
        }
      ),
    };
    var config = new ConfigurationBuilder()
      .AddInMemoryCollection(
        new Dictionary<string, string?>
        {
          ["TorqueAI:BaseUrl"] = "https://example.test",
        }
      )
      .Build();
    var loads = await new TorqueApiService(
      new HttpClient(handler),
      config,
      new StubProviderCredentials(("apiKey", "test"))
    ).GetDispatchesAsync();
    Assert.Equal(1358, Assert.Single(loads).LoadNumber);
  }

  [Theory]
  [InlineData("sent", "completed")]
  [InlineData(" SENT ", "completed")]
  [InlineData("assigned", "assigned")]
  [InlineData("in_transit", "in_transit")]
  [InlineData("cancelled", "cancelled")]
  public async Task InvoiceSentMeansCompletedWithoutInventingActualFacts(
    string status,
    string expected
  )
  {
    var handler = new Handler
    {
      Body = JsonSerializer.Serialize(
        new
        {
          data = new[]
          {
            new
            {
              loadNumber = 123,
              status,
              stops = new[] { new { sequence = 1, job = "Drop Off" } },
            },
          },
          totalCount = 1,
          itemsPerPage = 500,
        }
      ),
    };
    var config = new ConfigurationBuilder()
      .AddInMemoryCollection(
        new Dictionary<string, string?>
        {
          ["TorqueAI:BaseUrl"] = "https://example.test",
        }
      )
      .Build();
    var provider = new TorqueDispatchProvider(
      new TorqueApiService(
        new HttpClient(handler),
        config,
        new StubProviderCredentials(("apiKey", "test"))
      )
    );
    var rows = await provider.GetDispatchesAsync(
      new(2026, 3, 1),
      new(2026, 3, 2)
    );
    var row = Assert.Single(rows);
    Assert.Equal(expected, row.Status);
    var stop = Assert.Single(row.Stops);
    Assert.Null(stop.ArrivedAt);
    Assert.Null(stop.DeliveredAt);
    Assert.Null(stop.DepartedAt);
  }

  private sealed class Handler : HttpMessageHandler
  {
    public string Body =
      """{"data":[{"loadNumber":1358}],"totalCount":1,"itemsPerPage":500}""";
    public List<string> Urls = [];

    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request,
      CancellationToken ct
    )
    {
      Urls.Add(request.RequestUri!.Query);
      return Task.FromResult(
        new HttpResponseMessage(HttpStatusCode.OK)
        {
          Content = new StringContent(Body),
        }
      );
    }
  }
}
