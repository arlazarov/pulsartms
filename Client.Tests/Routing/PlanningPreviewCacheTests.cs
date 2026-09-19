using System.Net;
using System.Net.Http.Json;
using Client.Models.DTO.Planning;
using Client.Services;
using Client.Tests.Support;

namespace Client.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Unit")]
public sealed class PlanningPreviewCacheTests
{
  [Theory]
  [InlineData("clear")]
  [InlineData("cancel")]
  [InlineData("newer-plan")]
  [InlineData("newer-empty")]
  public async Task LatePreviewCannotReplaceNewerPlanningOrRestoreClearedSessionData(
    string change
  )
  {
    var truck = Guid.NewGuid();
    var original = Result(truck, 1);
    var latest = Result(truck, 2);
    var reply = new TaskCompletionSource<HttpResponseMessage>(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    using var handler = new StubHttpMessageHandler(
      (request, _) =>
      {
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(
          $"/api/fleet/trucks/{truck}/planning/preview",
          request.RequestUri!.AbsolutePath
        );
        return reply.Task;
      }
    );
    using var client = new HttpClient(handler)
    {
      BaseAddress = new("https://local.test/"),
    };
    var cache = new PlanningDisplayCache(new ApiService(client));
    using var cancellation = new CancellationTokenSource();
    var pending = cache.ReadPreviewAsync(truck, cancellation.Token);
    var url = $"api/fleet/trucks/{truck}/planning";
    switch (change)
    {
      case "clear":
        cache.Clear();
        break;
      case "cancel":
        cancellation.Cancel();
        break;
      case "newer-plan":
        cache.Store(url, latest);
        break;
      case "newer-empty":
        cache.Store(url, latest with { State = null, DispatchId = null });
        break;
    }
    reply.SetResult(Ok(original));
    var result = await pending;
    if (change == "newer-plan")
    {
      Assert.Same(latest, result);
      Assert.Same(latest, cache.Get(url));
    }
    else
    {
      Assert.Null(result);
      Assert.Null(cache.Get(url));
    }
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task AuthoritativeIdentityWithoutGeometryIsReturnedButNotCachedAsAPlan(
    bool noCurrent
  )
  {
    var truck = Guid.NewGuid();
    var expected = Result(truck, 1) with { State = null };
    if (noCurrent)
      expected = expected with { DispatchId = null, LoadNumber = null };
    using var handler = new StubHttpMessageHandler(
      (_, _) => Task.FromResult(Ok(expected))
    );
    using var client = new HttpClient(handler)
    {
      BaseAddress = new("https://local.test/"),
    };
    var cache = new PlanningDisplayCache(new ApiService(client));
    var result = await cache.ReadPreviewAsync(truck, default);
    Assert.Equal(expected, result);
    Assert.Null(cache.Get($"api/fleet/trucks/{truck}/planning"));
  }

  [Fact]
  public async Task WritesForAnotherTruckAndItsDispatchAliasDoNotDiscardAValidColdPreview()
  {
    var truckA = Guid.NewGuid();
    var truckB = Guid.NewGuid();
    var previewB = Result(truckB, 1);
    var reply = new TaskCompletionSource<HttpResponseMessage>(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    using var handler = new StubHttpMessageHandler((_, _) => reply.Task);
    using var client = new HttpClient(handler)
    {
      BaseAddress = new("https://local.test/"),
    };
    var cache = new PlanningDisplayCache(new ApiService(client));
    var pending = cache.ReadPreviewAsync(truckB, default);
    var latestA = Result(truckA, 2);
    cache.Store($"api/fleet/trucks/{truckA}/planning", latestA);
    cache.Store(
      $"api/dispatch/{latestA.DispatchId}/planning/automatic",
      latestA
    );
    reply.SetResult(Ok(previewB));
    var result = await pending;
    Assert.Equal(truckB, result!.TruckId);
    Assert.Equal(1, result.State!.Plan!.Version);
    Assert.Same(result, cache.Get($"api/fleet/trucks/{truckB}/planning"));
    Assert.Same(latestA, cache.Get($"api/fleet/trucks/{truckA}/planning"));
  }

  [Fact]
  public async Task FuelRecalculationSupersedesItsOwnReadsWithoutDiscardingAnotherTruckPreview()
  {
    var truckA = Guid.NewGuid();
    var truckB = Guid.NewGuid();
    var latestA = Result(truckA, 2);
    var previewB = Result(truckB, 1);
    var oldA = latestA with { State = Result(truckA, 1).State };
    var replyA = new TaskCompletionSource<HttpResponseMessage>(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    var replyB = new TaskCompletionSource<HttpResponseMessage>(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    using var handler = new StubHttpMessageHandler(
      (request, _) =>
        request.RequestUri!.AbsolutePath.Contains(
          truckB.ToString(),
          StringComparison.Ordinal
        )
          ? replyB.Task
          : replyA.Task
    );
    using var client = new HttpClient(handler)
    {
      BaseAddress = new("https://local.test/"),
    };
    var cache = new PlanningDisplayCache(new ApiService(client));
    var pendingA = cache.ReadPreviewAsync(truckA, default);
    var pendingB = cache.ReadPreviewAsync(truckB, default);
    cache.StoreRecalculated(latestA);
    Assert.Same(latestA, cache.Get($"api/fleet/trucks/{truckA}/planning"));
    replyA.SetResult(Ok(oldA));
    replyB.SetResult(Ok(previewB));
    Assert.Same(latestA, await pendingA);
    var resultB = await pendingB;
    Assert.NotNull(resultB);
    Assert.Equal(truckB, resultB.TruckId);
    Assert.Same(resultB, cache.Get($"api/fleet/trucks/{truckB}/planning"));
    Assert.Same(
      latestA,
      cache.Get($"api/dispatch/{latestA.DispatchId}/planning/automatic")
    );
  }

  [Fact]
  public async Task FuelRecalculationSupersedesOnlyMatchingPendingRefreshes()
  {
    var latestA = Result(Guid.NewGuid(), 2);
    var latestB = Result(Guid.NewGuid(), 3);
    var urlA = $"api/fleet/trucks/{latestA.TruckId}/planning";
    var urlB = $"api/fleet/trucks/{latestB.TruckId}/planning";
    var replyA = new TaskCompletionSource<HttpResponseMessage>(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    var replyB = new TaskCompletionSource<HttpResponseMessage>(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    using var handler = new StubHttpMessageHandler(
      (request, _) =>
        request.RequestUri!.AbsolutePath.Contains(
          latestB.TruckId.ToString(),
          StringComparison.Ordinal
        )
          ? replyB.Task
          : replyA.Task
    );
    using var client = new HttpClient(handler)
    {
      BaseAddress = new("https://local.test/"),
    };
    var cache = new PlanningDisplayCache(new ApiService(client));
    var pendingA = cache.RefreshAsync(urlA, default);
    var pendingB = cache.RefreshAsync(urlB, default);
    cache.StoreRecalculated(latestA);
    replyA.SetResult(Ok(Result(latestA.TruckId, 1)));
    replyB.SetResult(Ok(latestB));
    await Task.WhenAll(pendingA, pendingB);
    Assert.Same(latestA, cache.Get(urlA));
    Assert.Equal(3, cache.Get(urlB)!.State!.Plan!.Version);
  }

  [Fact]
  public async Task BulkPreloadOverflowDoesNotBlockAnExplicitTruckPreview()
  {
    var truck = Guid.NewGuid();
    var saved = Result(truck, 1);
    var bulkReply = new TaskCompletionSource<HttpResponseMessage>(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    using var handler = new StubHttpMessageHandler(
      (request, _) =>
        request.RequestUri!.AbsolutePath.EndsWith(
          "/planning/preview",
          StringComparison.Ordinal
        )
          ? Task.FromResult(Ok(saved))
          : bulkReply.Task
    );
    using var client = new HttpClient(handler)
    {
      BaseAddress = new("https://local.test/"),
    };
    var cache = new PlanningDisplayCache(new ApiService(client));
    var bulk = cache.PreloadAsync();
    for (var i = 0; i < 257; i++)
      cache.Store($"api/fleet/trucks/{Guid.NewGuid()}/planning", null);
    var result = await cache.ReadPreviewAsync(truck, default);
    Assert.NotNull(result);
    Assert.Same(result, cache.Get($"api/fleet/trucks/{truck}/planning"));
    bulkReply.SetResult(
      new(HttpStatusCode.OK)
      {
        Content = JsonContent.Create(
          new { success = true, response = new[] { saved } }
        ),
      }
    );
    await bulk;
    Assert.Same(result, cache.Get($"api/fleet/trucks/{truck}/planning"));
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task EarlierBulkReplyCannotSupersedeAnActiveExplicitPreview(
    bool noCurrent
  )
  {
    var truck = Guid.NewGuid();
    var old = Result(truck, 1);
    var latest = Result(truck, 2);
    if (noCurrent)
      latest = latest with
      {
        DispatchId = null,
        LoadNumber = null,
        State = null,
      };
    var bulkReply = new TaskCompletionSource<HttpResponseMessage>(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    var directReply = new TaskCompletionSource<HttpResponseMessage>(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    using var handler = new StubHttpMessageHandler(
      (request, _) =>
        request.RequestUri!.AbsolutePath.EndsWith(
          "/planning/preview",
          StringComparison.Ordinal
        )
          ? directReply.Task
          : bulkReply.Task
    );
    using var client = new HttpClient(handler)
    {
      BaseAddress = new("https://local.test/"),
    };
    var cache = new PlanningDisplayCache(new ApiService(client));
    var bulk = cache.PreloadAsync();
    var direct = cache.ReadPreviewAsync(truck, default);
    bulkReply.SetResult(
      new(HttpStatusCode.OK)
      {
        Content = JsonContent.Create(
          new { success = true, response = new[] { old } }
        ),
      }
    );
    await bulk;
    var url = $"api/fleet/trucks/{truck}/planning";
    Assert.Null(cache.Get(url));
    directReply.SetResult(Ok(latest));
    var result = await direct;
    Assert.NotNull(result);
    Assert.Equal(latest.DispatchId, result.DispatchId);
    if (noCurrent)
    {
      Assert.Null(result.State);
      Assert.Null(cache.Get(url));
    }
    else
    {
      Assert.Equal(2, result.State!.Plan!.Version);
      Assert.Same(result, cache.Get(url));
    }
  }

  private static AutomaticPlanningResult Result(Guid truck, int version) =>
    new(
      truck,
      Guid.NewGuid(),
      1358,
      new(
        new(),
        new()
        {
          Id = Guid.NewGuid(),
          TruckId = truck,
          Version = version,
        },
        null,
        null,
        null,
        true
      ),
      null
    );

  private static HttpResponseMessage Ok(AutomaticPlanningResult response) =>
    new(HttpStatusCode.OK)
    {
      Content = JsonContent.Create(new { success = true, response }),
    };
}
