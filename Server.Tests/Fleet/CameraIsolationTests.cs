using System.Net;
using System.Text.Json;
using Application.Caching;
using Application.Features.Integrations.Models;
using Application.Features.Synchronization.Options;
using Infrastructure.Integrations.Samsara;
using Microsoft.Extensions.Options;
using Server.Tests.Support;

namespace Server.Tests.Fleet;

[Trait("Category", "Fleet")]
[Trait("Kind", "Integration")]
public sealed class CameraIsolationTests
{
  [Fact]
  public async Task SameVehicleAndCredentialRemainIsolatedByCompany()
  {
    var companies = new TestCompany();
    using var cache = new ReadCache(
      Options.Create(new SynchronizationOptions()),
      companies
    );
    using var http = new CameraHttp();
    using var client = new HttpClient(http);
    var provider = new SamsaraTruckCameraProvider(
      new SamsaraApiService(
        client,
        new StubProviderCredentials(("apiKey", "first"))
      ),
      cache,
      companies
    );
    await provider.LatestAsync("shared-vehicle", default);
    using (companies.As(Guid.NewGuid()))
    {
      await provider.LatestAsync("shared-vehicle", default);
      await provider.LatestAsync("shared-vehicle", default);
    }
    await provider.LatestAsync("shared-vehicle", default);
    Assert.Equal(2, http.Calls);
  }

  [Fact]
  public async Task RotationRejectsOldRetrievalAndLateImageCannotReplaceNew()
  {
    var companies = new TestCompany();
    var credentials = new StubProviderCredentials(("apiKey", "first"));
    using var cache = new ReadCache(
      Options.Create(new SynchronizationOptions()),
      companies
    );
    using var http = new CameraHttp { HoldFirst = true };
    using var client = new HttpClient(http);
    var provider = new SamsaraTruckCameraProvider(
      new SamsaraApiService(client, credentials),
      cache,
      companies
    );
    var retrieval = await provider.RequestAsync(
      "shared-vehicle",
      DateTimeOffset.UtcNow,
      default
    );
    var oldRead = provider.LatestAsync("shared-vehicle", default);
    await http.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
    credentials.Values = new IntegrationCredentialValues(
      new Dictionary<string, string> { ["apiKey"] = "second" }
    );
    var rejected = await provider.GetAsync(
      "shared-vehicle",
      retrieval,
      default
    );
    Assert.Equal("expired", rejected.Status);
    var currentRead = provider.LatestAsync("shared-vehicle", default);
    http.Release.SetResult();
    Assert.Equal("https://example.invalid/first.jpg", (await oldRead).Url);
    Assert.Equal("https://example.invalid/second.jpg", (await currentRead).Url);
    Assert.Equal(
      "https://example.invalid/second.jpg",
      (await provider.LatestAsync("shared-vehicle", default)).Url
    );
    Assert.Equal(2, http.Calls);
  }

  [Fact]
  public async Task OverlappingReadersShareOneProviderRequest()
  {
    var companies = new TestCompany();
    using var cache = new ReadCache(
      Options.Create(new SynchronizationOptions()),
      companies
    );
    using var http = new CameraHttp { HoldFirst = true };
    using var client = new HttpClient(http);
    var provider = new SamsaraTruckCameraProvider(
      new SamsaraApiService(
        client,
        new StubProviderCredentials(("apiKey", "first"))
      ),
      cache,
      companies
    );
    var readers = Enumerable
      .Range(0, 32)
      .Select(_ => provider.LatestAsync("shared-vehicle", default))
      .ToArray();
    await http.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Assert.Equal(1, http.Calls);
    http.Release.SetResult();
    var images = await Task.WhenAll(readers);
    Assert.All(
      images,
      x => Assert.Equal("https://example.invalid/first.jpg", x.Url)
    );
    Assert.Equal(1, http.Calls);
  }

  private sealed class CameraHttp : HttpMessageHandler
  {
    public bool HoldFirst { get; init; }
    public int Calls;
    public TaskCompletionSource Started { get; } =
      new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } =
      new(TaskCreationOptions.RunContinuationsAsynchronously);

    protected override async Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request,
      CancellationToken ct
    )
    {
      if (request.Method == HttpMethod.Post)
        return new(HttpStatusCode.OK)
        {
          Content = new StringContent("""{"data":{"retrievalId":"capture"}}"""),
        };
      var credential = request.Headers.Authorization!.Parameter!;
      Interlocked.Increment(ref Calls);
      if (credential == "first" && HoldFirst)
      {
        Started.TrySetResult();
        await Release.Task.WaitAsync(ct);
      }
      return new(HttpStatusCode.OK)
      {
        Content = new StringContent(
          JsonSerializer.Serialize(
            new
            {
              data = new
              {
                media = new[]
                {
                  new
                  {
                    vehicleId = "shared-vehicle",
                    input = "dashcamRoadFacing",
                    startTime = "2026-09-26T12:00:00Z",
                    urlInfo = new
                    {
                      url = $"https://example.invalid/{credential}.jpg",
                    },
                  },
                },
              },
            }
          )
        ),
      };
    }
  }
}
