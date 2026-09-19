using System.Net;
using System.Net.Http.Json;
using Client.Models.DTO;
using Client.Models.DTO.Fleet;
using Client.Pages.FleetMap;
using Client.Tests.Support;

namespace Client.Tests.Fleet;

[Trait("Category", "Fleet")]
[Trait("Kind", "Unit")]
public sealed class FleetStationLayerTests
{
  private static readonly DateOnly Date = new(2026, 9, 8);

  [Fact]
  public async Task FailureRetainsPreviousStationsAndDateUntilSuccessfulRetry()
  {
    var fail = false;
    using var http = Client(
      (_, _) =>
        Task.FromResult(
          fail
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : Success()
        )
    );
    var map = new MapInteropStub();
    using var layer = new FleetStationLayer(http, map);
    await layer.LoadAsync(Date, () => true, default);
    Assert.Equal(Date, layer.LoadedDate);
    var call = Assert.Single(map.Calls);
    Assert.Equal("2026-09-08", call.Args![1]);
    Assert.Equal(true, call.Args[2]);
    fail = true;
    await layer.LoadAsync(Date.AddDays(1), () => false, default);
    Assert.Equal(Date, layer.LoadedDate);
    Assert.NotNull(layer.Error);
    Assert.Single(map.Calls);
    fail = false;
    await layer.LoadAsync(Date.AddDays(1), () => false, default);
    Assert.Equal(Date.AddDays(1), layer.LoadedDate);
    Assert.Null(layer.Error);
    Assert.Equal(2, map.Calls.Count);
  }

  [Fact]
  public async Task DateChangeCancelsEarlierRequestAndItsLateResponseCannotReplaceTheLatestStations()
  {
    var pending = new TaskCompletionSource<HttpResponseMessage>(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    CancellationToken previous = default;
    using var http = Client(
      (request, token) =>
      {
        if (
          request.RequestUri!.Query.Contains(
            "2026-09-08",
            StringComparison.Ordinal
          )
        )
        {
          previous = token;
          return pending.Task;
        }
        return Task.FromResult(Success());
      }
    );
    var map = new MapInteropStub();
    using var layer = new FleetStationLayer(http, map);
    var first = layer.LoadAsync(Date, () => false, default);
    await layer.LoadAsync(Date.AddDays(1), () => false, default);
    Assert.True(previous.IsCancellationRequested);
    pending.SetResult(Success());
    await first;
    Assert.Equal(Date.AddDays(1), layer.LoadedDate);
    Assert.Single(map.Calls);
    Assert.Null(layer.Error);
  }

  [Theory]
  [InlineData("hidden")]
  [InlineData("disposed")]
  [InlineData("lifetime")]
  public async Task CancelledLoadsNeverPublishOrReportAnError(string reason)
  {
    var pending = new TaskCompletionSource<HttpResponseMessage>(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    using var http = Client((_, _) => pending.Task);
    using var lifetime = new CancellationTokenSource();
    var map = new MapInteropStub();
    using var layer = new FleetStationLayer(http, map);
    var load = layer.LoadAsync(Date, () => false, lifetime.Token);
    if (reason == "hidden")
      layer.Cancel();
    else if (reason == "disposed")
      layer.Dispose();
    else
      await lifetime.CancelAsync();
    pending.SetResult(Success());
    await load;
    Assert.Null(layer.LoadedDate);
    Assert.Null(layer.Error);
    Assert.Empty(map.Calls);
  }

  [Fact]
  public async Task CancelledPublicationDoesNotMarkTheDateAsLoaded()
  {
    using var http = Client((_, _) => Task.FromResult(Success()));
    var pending = new TaskCompletionSource<object?>(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    var map = new MapInteropStub { Respond = (_, _) => pending.Task };
    using var layer = new FleetStationLayer(http, map);
    var load = layer.LoadAsync(Date, () => false, default);
    Assert.Single(map.Calls);
    layer.Cancel();
    pending.SetResult(null);
    await load;
    Assert.Null(layer.LoadedDate);
    Assert.Null(layer.Error);
  }

  [Fact]
  public async Task PublicationUsesTheLatestIftaToggleEvenWhenItChangedDuringTheRequest()
  {
    var pending = new TaskCompletionSource<HttpResponseMessage>(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    using var http = Client((_, _) => pending.Task);
    var map = new MapInteropStub();
    using var layer = new FleetStationLayer(http, map);
    var useIfta = false;
    var load = layer.LoadAsync(Date, () => useIfta, default);
    useIfta = true;
    pending.SetResult(Success());
    await load;
    Assert.Equal(true, Assert.Single(map.Calls).Args![2]);
  }

  private static HttpClient Client(
    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send
  ) =>
    new(new StubHttpMessageHandler(send))
    {
      BaseAddress = new("http://fixture.invalid"),
    };

  [Fact]
  public async Task OverviewLoadsIndependentlyOfStationVisibilityAndReusesTheDay()
  {
    var calls = 0;
    using var http = Client(
      (request, _) =>
      {
        Assert.Equal(
          "/api/fuel/price-overview",
          request.RequestUri!.AbsolutePath
        );
        calls++;
        return Task.FromResult(PriceSuccess());
      }
    );
    var map = new MapInteropStub();
    using var layer = new FleetStationLayer(http, map);
    await layer.LoadPricesAsync(Date, () => false, default);
    layer.Cancel();
    await layer.LoadPricesAsync(Date, () => true, default);
    Assert.Equal(1, calls);
    Assert.Null(layer.LoadedDate);
    Assert.Null(layer.PriceError);
    Assert.All(map.Calls, call => Assert.Equal("setPriceOverview", call.Name));
    Assert.Null(map.Calls.First().Args![0]);
    Assert.Equal(true, map.Calls.Last().Args![2]);
    Assert.Single(
      Assert.IsType<List<FuelMapPriceDto>>(map.Calls.Last().Args![0])
    );
  }

  [Fact]
  public async Task ReturningToCachedDateCancelsPendingOtherDateBeforeItCanChangeColors()
  {
    var pending = new TaskCompletionSource<HttpResponseMessage>(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    CancellationToken otherDate = default;
    using var http = Client(
      (request, token) =>
      {
        if (
          request.RequestUri!.Query.Contains(
            "2026-09-09",
            StringComparison.Ordinal
          )
        )
        {
          otherDate = token;
          return pending.Task;
        }
        return Task.FromResult(PriceSuccess());
      }
    );
    var map = new MapInteropStub();
    using var layer = new FleetStationLayer(http, map);
    await layer.LoadPricesAsync(Date, () => false, default);
    var other = layer.LoadPricesAsync(Date.AddDays(1), () => false, default);
    await layer.LoadPricesAsync(Date, () => false, default);
    var publications = map.Calls.Count;
    pending.SetResult(PriceSuccess());
    await other;
    Assert.True(otherDate.IsCancellationRequested);
    Assert.Equal(publications, map.Calls.Count);
    Assert.Equal("2026-09-08", map.Calls.Last().Args![1]);
    Assert.Null(layer.PriceError);
  }

  [Fact]
  public async Task FailedOverviewClearsOldColorsAndCanBeRetried()
  {
    var fail = true;
    using var http = Client(
      (_, _) =>
        Task.FromResult(
          fail
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : PriceSuccess()
        )
    );
    var map = new MapInteropStub();
    using var layer = new FleetStationLayer(http, map);
    await layer.LoadPricesAsync(Date, () => false, default);
    Assert.NotNull(layer.PriceError);
    Assert.Null(Assert.Single(map.Calls).Args![0]);
    fail = false;
    await layer.LoadPricesAsync(Date, () => false, default);
    Assert.Null(layer.PriceError);
    Assert.NotNull(map.Calls.Last().Args![0]);
  }

  [Fact]
  public async Task DisposingPendingOverviewPreventsLatePricePublication()
  {
    var pending = new TaskCompletionSource<HttpResponseMessage>(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    using var http = Client((_, _) => pending.Task);
    var map = new MapInteropStub();
    using var layer = new FleetStationLayer(http, map);
    var read = layer.LoadPricesAsync(Date, () => false, default);
    layer.Dispose();
    pending.SetResult(PriceSuccess());
    await read;
    Assert.Single(map.Calls);
    Assert.Null(layer.PriceError);
  }

  private static HttpResponseMessage PriceSuccess() =>
    new(HttpStatusCode.OK)
    {
      Content = JsonContent.Create(
        new ApiResponse<List<FuelMapPriceDto>>
        {
          Success = true,
          Response = [new(Guid.NewGuid(), "USD", 3.1m, 2.5m)],
        }
      ),
    };

  private static HttpResponseMessage Success() =>
    new(HttpStatusCode.OK)
    {
      Content = JsonContent.Create(
        new ApiResponse<List<FuelStationMapDto>>
        {
          Success = true,
          Response = [],
        }
      ),
    };
}
