using System.Net;
using System.Text.Json;
using Application.Features.Fleet.Commands.RequestTruckCamera;
using Application.Features.Fleet.Interfaces;
using Application.Features.Fleet.Queries;
using Application.Features.Fleet.Queries.GetLatestTruckCamera;
using Application.Features.Fleet.Queries.GetTruckCamera;
using Domain.Entities.Fleet;
using Infrastructure.Integrations.Samsara;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Server.Tests.Support;

namespace Server.Tests.Fleet;

[Trait("Category", "Fleet")]
public class TruckCameraTests
{
  [Theory]
  [InlineData("{\"data\":{\"media\":null}}")]
  [InlineData("{\"data\":{\"media\":[]}}")]
  [InlineData("{\"data\":{}}")]
  [InlineData("{\"data\":null}")]
  public async Task EmptyMediaDoesNotFailOpeningOrPolling(string body)
  {
    using var client = new HttpClient(new EmptyCameraHttp(body));
    using var cache = new MemoryCache(new MemoryCacheOptions());
    var provider = new SamsaraTruckCameraProvider(
      new SamsaraApiService(
        client,
        new StubProviderCredentials(("apiKey", "test"))
      ),
      cache
    );
    var latest = await provider.LatestAsync("vehicle", default);
    Assert.Null(latest.Url);
    var pending = await provider.GetAsync("vehicle", "retrieval", default);
    Assert.Equal("pending", pending.Status);
    Assert.Null(pending.Url);
  }

  private sealed class EmptyCameraHttp(string body) : HttpMessageHandler
  {
    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request,
      CancellationToken ct
    ) =>
      Task.FromResult(
        new HttpResponseMessage(HttpStatusCode.OK)
        {
          Content = new StringContent(body),
        }
      );
  }

  [Fact]
  public async Task RefreshCreatesNewCaptureAndCannotBeReadForAnotherTruck()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    var truck = new Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = "vehicle",
      UnitNumber = "123",
      IsActive = true,
    };
    db.Trucks.Add(truck);
    await db.SaveChangesAsync();
    using var cache = new MemoryCache(new MemoryCacheOptions());
    var provider = new FakeCamera();
    var handler = new RequestTruckCameraHandler(db, provider, cache);
    var latest = new GetLatestTruckCameraHandler(db, provider);
    var retrieval = new GetTruckCameraHandler(provider, cache);
    Assert.True(
      (
        await latest.Handle(new GetLatestTruckCameraQuery(truck.Id), default)
      ).Success
    );
    Assert.Equal(0, provider.Requests);
    Assert.False(
      (
        await latest.Handle(
          new GetLatestTruckCameraQuery(Guid.NewGuid()),
          default
        )
      ).Success
    );
    var first = await handler.Handle(
      new RequestTruckCameraCommand(truck.Id),
      default
    );
    var second = await handler.Handle(
      new RequestTruckCameraCommand(truck.Id),
      default
    );
    Assert.True(first.Success);
    Assert.NotEqual(first.Response, second.Response);
    Assert.Equal(2, provider.Requests);
    Assert.False(
      (
        await retrieval.Handle(
          new GetTruckCameraQuery(Guid.NewGuid(), first.Response),
          default
        )
      ).Success
    );
    Assert.Equal(
      "pending",
      (
        await retrieval.Handle(
          new GetTruckCameraQuery(truck.Id, first.Response),
          default
        )
      )
        .Response!
        .Status
    );
    Assert.False(
      (
        await handler.Handle(
          new RequestTruckCameraCommand(Guid.NewGuid()),
          default
        )
      ).Success
    );
  }

  [Fact]
  public async Task ProviderRequestsRoadImageAndUsesActualCaptureTime()
  {
    using var client = new HttpClient(new CameraHttp());
    using var cache = new MemoryCache(new MemoryCacheOptions());
    var provider = new SamsaraTruckCameraProvider(
      new SamsaraApiService(
        client,
        new StubProviderCredentials(("apiKey", "test"))
      ),
      cache
    );
    Assert.Equal(
      "retrieval",
      await provider.RequestAsync("vehicle", DateTimeOffset.UtcNow, default)
    );
    var image = await provider.GetAsync("vehicle", "retrieval", default);
    Assert.Equal("https://example.com/image.jpg", image.Url);
    Assert.Equal(
      DateTimeOffset.Parse("2026-09-07T12:00:00Z"),
      image.CapturedAt
    );
  }

  [Fact]
  public async Task PendingRetrievalShowsCachedPreviousImageWithoutClaimingItIsFresh()
  {
    var http = new CameraHttp(true);
    using var client = new HttpClient(http);
    using var cache = new MemoryCache(new MemoryCacheOptions());
    var provider = new SamsaraTruckCameraProvider(
      new SamsaraApiService(
        client,
        new StubProviderCredentials(("apiKey", "test"))
      ),
      cache
    );
    var first = await provider.GetAsync("vehicle", "retrieval", default);
    var second = await provider.GetAsync("vehicle", "retrieval", default);
    Assert.Equal("pending", first.Status);
    Assert.NotNull(first.Url);
    Assert.Equal(first, second);
    Assert.Equal(1, http.ListCalls);
  }

  private sealed class FakeCamera : ITruckCameraProvider
  {
    public int Requests;

    public Task<CameraImage> LatestAsync(
      string vehicleId,
      CancellationToken ct
    ) => Task.FromResult(new CameraImage("pending"));

    public Task<string> RequestAsync(
      string vehicleId,
      DateTimeOffset time,
      CancellationToken ct
    )
    {
      Requests++;
      return Task.FromResult("retrieval");
    }

    public Task<CameraImage> GetAsync(
      string vehicleId,
      string retrievalId,
      CancellationToken ct
    ) => Task.FromResult(new CameraImage("pending"));
  }

  private sealed class CameraHttp(bool pending = false) : HttpMessageHandler
  {
    public int ListCalls;

    protected override async Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request,
      CancellationToken ct
    )
    {
      if (request.Method == HttpMethod.Post)
      {
        using var json = JsonDocument.Parse(
          await request.Content!.ReadAsStringAsync(ct)
        );
        var data = json.RootElement;
        Assert.Equal("image", data.GetProperty("mediaType").GetString());
        Assert.Equal(
          "dashcamRoadFacing",
          data.GetProperty("inputs")[0].GetString()
        );
        Assert.Equal(
          data.GetProperty("startTime").GetString(),
          data.GetProperty("endTime").GetString()
        );
      }
      var isRetrieval = request.RequestUri!.AbsolutePath.EndsWith("/retrieval");
      if (!isRetrieval)
        ListCalls++;
      if (pending && isRetrieval && request.Method == HttpMethod.Get)
        return new(HttpStatusCode.OK)
        {
          Content = new StringContent(
            """{"data":{"media":[{"vehicleId":"vehicle","input":"dashcamRoadFacing","mediaType":"image","status":"pending"}]}}"""
          ),
        };
      return new(HttpStatusCode.OK)
      {
        Content = new StringContent(
          request.Method == HttpMethod.Post
            ? """{"data":{"retrievalId":"retrieval"}}"""
            : """{"data":{"media":[{"vehicleId":"vehicle","input":"dashcamRoadFacing","mediaType":"image","status":"available","startTime":"2026-09-07T12:00:00Z","urlInfo":{"url":"https://example.com/image.jpg"}}]}}"""
        ),
      };
    }
  }
}
