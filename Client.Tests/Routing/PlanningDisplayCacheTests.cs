using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;
using Client.Models.DTO;
using Client.Models.DTO.Planning;
using Client.Pages.FleetMap;
using Client.Services;
using Microsoft.Extensions.Time.Testing;

namespace Client.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Unit")]
public class PlanningDisplayCacheTests
{
  [Fact]
  public async Task ExpiredCompleteHttpReplyRetainsBothDisplayLayersAcrossTheObservedDeadlineTimeline()
  {
    var calculatedAt = new DateTime(
      2026,
      9,
      8,
      12,
      1,
      6,
      268,
      DateTimeKind.Utc
    );
    var clock = new FakeTimeProvider(new DateTimeOffset(calculatedAt));
    using var handler = new ControlledHandler();
    using var client = new HttpClient(handler)
    {
      BaseAddress = new("https://local.test/"),
    };
    var cache = new PlanningDisplayCache(new ApiService(client), clock);
    var saved = ForecastResult(calculatedAt);
    var state = saved.State!;
    var stop = state.Plan!.Stops[0];
    var arrival = new ArrivalDisplayMemory();
    var route = new FleetRouteDisplayMemory();
    arrival.Update(saved.DispatchId, stop, state.Eta);
    route.Update(state, calculatedAt);
    route.RecordProgress(790, 210);
    cache.Store("planning", saved);

    var pendingAt = new DateTime(2026, 9, 8, 12, 3, 16, 322, DateTimeKind.Utc);
    var pending = state with
    {
      Progress = null,
      Eta = state.Eta! with { RouteUpdatePending = true },
    };
    route.Update(pending, pendingAt);
    arrival.Update(saved.DispatchId, stop, pending.Eta);
    Assert.Same(
      state.Eta,
      arrival.Display(pending.Eta, pendingAt, refreshing: true)
    );

    var receivedAt = new DateTime(2026, 9, 8, 12, 3, 16, 380, DateTimeKind.Utc);
    clock.SetUtcNow(new(receivedAt));
    var read = cache.RefreshAsync("planning", default);
    (await handler.Next()).SetResult(
      ControlledHandler.Reply(
        saved with
        {
          State = state with { Progress = null },
        }
      )
    );
    var late = (await read).Response!;
    Assert.True(late.State!.Eta!.RouteUpdatePending);
    Assert.Same(late, cache.Get("planning"));
    Assert.Equal(state.Eta!.CalculatedAt, late.State.Eta.CalculatedAt);
    Assert.Equal(state.Eta.ValidUntil, late.State.Eta.ValidUntil);
    Assert.False(state.Eta.RouteUpdatePending);
    route.Update(late.State, receivedAt);
    arrival.Update(saved.DispatchId, stop, late.State.Eta);
    Assert.Same(
      state.Eta,
      arrival.Display(late.State.Eta, receivedAt, refreshing: false)
    );
    var retained = route.Display(late.State, receivedAt, refreshing: false)!;
    Assert.Equal(state.Eta.Stops, retained.Eta!.Stops);
    Assert.Equal(state.Eta.ValidUntil, retained.Eta.ValidUntil);
    Assert.Equal(790, retained.Progress!.RemainingMiles);
    Assert.Equal(210, retained.Progress.ProgressMiles);

    var readyAt = new DateTime(2026, 9, 8, 12, 3, 16, 343, DateTimeKind.Utc);
    var ready = saved with
    {
      State = state with
      {
        Eta = state.Eta with
        {
          CalculatedAt = readyAt,
          ValidUntil = readyAt.AddMinutes(2),
          Stops =
          [
            state.Eta.Stops[0] with
            {
              Arrival = state.Eta.Stops[0].Arrival.AddMinutes(5),
            },
          ],
        },
      },
    };
    var replacementAt = new DateTime(
      2026,
      9,
      8,
      12,
      3,
      26,
      382,
      DateTimeKind.Utc
    );
    clock.SetUtcNow(new(replacementAt));
    var refresh = cache.RefreshAsync("planning", default);
    (await handler.Next()).SetResult(ControlledHandler.Reply(ready));
    var replacement = (await refresh).Response!;
    Assert.False(replacement.State!.Eta!.RouteUpdatePending);
    route.Update(replacement.State, replacementAt);
    arrival.Update(saved.DispatchId, stop, replacement.State.Eta);
    Assert.Same(
      replacement.State.Eta,
      arrival.Display(replacement.State.Eta, replacementAt)
    );
    Assert.Same(
      replacement.State,
      route.Display(replacement.State, replacementAt)
    );
  }

  [Fact]
  public async Task NewerButExpiredRepliesCannotOverwriteAFresherDisplayOrRenewTheOriginalGraceDeadline()
  {
    var now = DateTime.UnixEpoch;
    var clock = new FakeTimeProvider(new DateTimeOffset(now));
    using var handler = new ControlledHandler();
    using var client = new HttpClient(handler)
    {
      BaseAddress = new("https://local.test/"),
    };
    var cache = new PlanningDisplayCache(new ApiService(client), clock);
    var saved = ForecastResult(now);
    var state = saved.State!;
    var arrival = new ArrivalDisplayMemory();
    var route = new FleetRouteDisplayMemory();
    arrival.Update(saved.DispatchId, state.Plan!.Stops[0], state.Eta);
    route.Update(state, now);
    AutomaticPlanningResult? late = null;
    foreach (var minute in new[] { 1, 16 })
    {
      clock.SetUtcNow(new(now.AddMinutes(minute)));
      var expired = saved with
      {
        State = state with
        {
          Eta = state.Eta! with
          {
            CalculatedAt = now.AddMinutes(minute).AddSeconds(-30),
            ValidUntil = now.AddMinutes(minute).AddSeconds(-10),
            Stops = [state.Eta.Stops[0] with { Arrival = now.AddHours(20) }],
          },
        },
      };
      var read = cache.RefreshAsync("planning", default);
      (await handler.Next()).SetResult(ControlledHandler.Reply(expired));
      late = (await read).Response!;
      route.Update(late.State, clock.GetUtcNow().UtcDateTime);
      arrival.Update(saved.DispatchId, state.Plan.Stops[0], late.State!.Eta);
      Assert.Same(
        state.Eta,
        arrival.Display(late.State.Eta, clock.GetUtcNow().UtcDateTime)
      );
      Assert.Equal(
        state.Eta!.ValidUntil,
        route
          .Display(late.State, clock.GetUtcNow().UtcDateTime)!
          .Eta!.ValidUntil
      );
    }
    Assert.Same(
      state.Eta,
      arrival.Display(late!.State!.Eta, now.AddMinutes(17).AddTicks(-1))
    );
    Assert.Null(arrival.Display(late.State.Eta, now.AddMinutes(17)));
    Assert.False(route.IsRetaining(late.State, now.AddMinutes(17)));
    Assert.False(
      FleetRouteDisplayMemory.CanDisplay(
        route.Display(late.State, now.AddMinutes(17))!.Eta,
        now.AddMinutes(17)
      )
    );
  }

  [Fact]
  public void PassiveCachedRerendersDoNotClassifyAnExpiredForecastAsANewPendingResponse()
  {
    var now = DateTime.UnixEpoch;
    var clock = new FakeTimeProvider(new DateTimeOffset(now));
    using var client = new HttpClient
    {
      BaseAddress = new("https://local.test/"),
    };
    var cache = new PlanningDisplayCache(new ApiService(client), clock);
    var saved = ForecastResult(now);
    var arrival = new ArrivalDisplayMemory();
    var route = new FleetRouteDisplayMemory();
    cache.Store("planning", saved);
    arrival.Update(
      saved.DispatchId,
      saved.State!.Plan!.Stops[0],
      saved.State.Eta
    );
    route.Update(saved.State, now);
    clock.SetUtcNow(new(now.AddMinutes(2)));
    var cached = cache.Get("planning")!;
    Assert.Same(saved, cached);
    Assert.False(cached.State!.Eta!.RouteUpdatePending);
    arrival.Update(
      cached.DispatchId,
      cached.State.Plan!.Stops[0],
      cached.State.Eta with
      {
        Stops = cached.State.Eta.Stops.ToArray(),
      }
    );
    route.Update(cached.State, clock.GetUtcNow().UtcDateTime);
    Assert.Null(
      arrival.Display(cached.State.Eta, clock.GetUtcNow().UtcDateTime)
    );
    Assert.False(
      route.IsRetaining(cached.State, clock.GetUtcNow().UtcDateTime)
    );
    Assert.False(
      FleetRouteDisplayMemory.CanDisplay(
        route.Display(cached.State, clock.GetUtcNow().UtcDateTime)!.Eta,
        clock.GetUtcNow().UtcDateTime
      )
    );
  }

  [Theory]
  [InlineData("fresh")]
  [InlineData("unavailable")]
  [InlineData("invalid")]
  [InlineData("completed")]
  public async Task OnlyValidExpiredIncompleteRouteForecastsAreClassifiedAsPending(
    string scenario
  )
  {
    var now = DateTime.UnixEpoch;
    var clock = new FakeTimeProvider(new DateTimeOffset(now.AddMinutes(3)));
    var result = ForecastResult(now);
    if (scenario == "fresh")
      result = result with
      {
        State = result.State! with
        {
          Eta = result.State.Eta! with { ValidUntil = now.AddMinutes(5) },
        },
      };
    if (scenario == "unavailable")
      result = result with
      {
        State = result.State! with
        {
          Eta = result.State.Eta! with
          {
            Stops = [],
            UnavailableReason = "GPS unavailable",
          },
        },
      };
    if (scenario == "invalid")
      result = result with
      {
        State = result.State! with
        {
          Eta = result.State.Eta! with
          {
            CalculatedAt = result.State.Eta.ValidUntil,
          },
        },
      };
    if (scenario == "completed")
      result.State!.Plan!.Tracking.AllStopsPassed = true;
    using var handler = new Handler(result);
    using var client = new HttpClient(handler)
    {
      BaseAddress = new("https://local.test/"),
    };
    var cache = new PlanningDisplayCache(new ApiService(client), clock);
    var response = await cache.RefreshAsync("planning", default);
    Assert.False(response.Response!.State!.Eta!.RouteUpdatePending);
    Assert.Equal(
      result.State!.Eta!.ValidUntil,
      response.Response.State.Eta.ValidUntil
    );
  }

  private static AutomaticPlanningResult ForecastResult(DateTime now)
  {
    var truck = Guid.NewGuid();
    var dispatch = Guid.NewGuid();
    var stop = new PlanStop(
      Guid.NewGuid(),
      "Delivery",
      "Warehouse",
      1,
      new(40, -80)
    );
    return new(
      truck,
      dispatch,
      1,
      new(
        new(),
        new()
        {
          Id = Guid.NewGuid(),
          TruckId = truck,
          DispatchId = dispatch,
          Stops = [stop],
          Tracking = new() { NextStopId = stop.Id },
        },
        new(200, 800, 1000, 0, false, false, now, null),
        null,
        null,
        true
      )
      {
        Eta = new(
          now,
          now.AddMinutes(2),
          [
            new(stop.Id, now.AddHours(1), "UTC", null, 0, 60, 0)
            {
              DispatchId = dispatch,
            },
          ],
          null,
          []
        ),
      },
      null
    );
  }

  [Fact]
  public async Task ForcedRevalidationSupersedesOnlyItsInflightReadAndKeepsOtherTrucksCached()
  {
    using var handler = new ControlledHandler();
    using var client = new HttpClient(handler)
    {
      BaseAddress = new("https://local.test/"),
    };
    var cache = new PlanningDisplayCache(new ApiService(client));
    AutomaticPlanningResult Result(int version) =>
      new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        1,
        new(
          new(),
          new() { Id = Guid.NewGuid(), Version = version },
          null,
          null,
          null,
          true
        ),
        null
      );
    cache.Store("other", Result(3));
    var first = cache.RefreshAsync("planning", default);
    var oldRequest = await handler.Next();
    var revalidated = cache.RefreshAsync("planning", default, force: true);
    var newRequest = await handler.Next();
    newRequest.SetResult(ControlledHandler.Reply(Result(2)));
    await revalidated;
    oldRequest.SetResult(ControlledHandler.Reply(Result(1)));
    await first;
    Assert.Equal(2, cache.Get("planning")!.State!.Plan!.Version);
    Assert.Equal(3, cache.Get("other")!.State!.Plan!.Version);
  }

  private sealed class ControlledHandler : HttpMessageHandler
  {
    private readonly Channel<
      TaskCompletionSource<HttpResponseMessage>
    > requests = Channel.CreateUnbounded<
      TaskCompletionSource<HttpResponseMessage>
    >();

    public Task<TaskCompletionSource<HttpResponseMessage>> Next() =>
      requests.Reader.ReadAsync().AsTask();

    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request,
      CancellationToken cancellationToken
    )
    {
      var pending = new TaskCompletionSource<HttpResponseMessage>(
        TaskCreationOptions.RunContinuationsAsynchronously
      );
      requests.Writer.TryWrite(pending);
      return pending.Task;
    }

    public static HttpResponseMessage Reply(AutomaticPlanningResult result) =>
      new(HttpStatusCode.OK)
      {
        Content = JsonContent.Create(
          new RequestResponseDTO<AutomaticPlanningResult>
          {
            Success = true,
            Response = result,
          }
        ),
      };
  }

  [Fact]
  public async Task PreloadMakesSavedRouteAvailableAndCannotOverwriteNewerSelection()
  {
    var truck = Guid.NewGuid();
    var dispatch = Guid.NewGuid();
    AutomaticPlanningResult Result(int version) =>
      new(
        truck,
        dispatch,
        1,
        new(
          new(),
          new() { Id = dispatch, Version = version },
          null,
          null,
          null,
          true
        ),
        null
      );
    using var handler = new PreviewHandler(Result(1));
    using var client = new HttpClient(handler)
    {
      BaseAddress = new("https://local.test/"),
    };
    var cache = new PlanningDisplayCache(new ApiService(client));
    var first = cache.PreloadAsync();
    var second = cache.PreloadAsync();
    var truckUrl = $"api/fleet/trucks/{truck}/planning";
    cache.Store(truckUrl, Result(2));
    handler.Release.SetResult();
    await Task.WhenAll(first, second);
    Assert.Equal(1, handler.Calls);
    Assert.Equal(2, cache.Get(truckUrl)!.State!.Plan!.Version);
    Assert.Equal(
      1,
      cache
        .Get($"api/dispatch/{dispatch}/planning/automatic")!
        .State!.Plan!.Version
    );
    await cache.PreloadAsync();
    Assert.Equal(1, handler.Calls);
  }

  [Theory]
  [InlineData(true, false)]
  [InlineData(false, true)]
  [InlineData(true, true)]
  public async Task LatePreloadCannotRestoreNewerNoPlanKeysButStillLoadsUnrelatedTrucks(
    bool truckChanged,
    bool dispatchChanged
  )
  {
    var truckA = Guid.NewGuid();
    var truckB = Guid.NewGuid();
    var unrelatedTruck = Guid.NewGuid();
    AutomaticPlanningResult Result(Guid truck) =>
      new(
        truck,
        Guid.NewGuid(),
        1,
        new(
          new(),
          new()
          {
            Id = Guid.NewGuid(),
            TruckId = truck,
            Version = 1,
          },
          null,
          null,
          null,
          true
        ),
        null
      );
    var oldA = Result(truckA);
    var oldB = Result(truckB);
    using var handler = new PreviewHandler(oldA, oldB);
    using var client = new HttpClient(handler)
    {
      BaseAddress = new("https://local.test/"),
    };
    var cache = new PlanningDisplayCache(new ApiService(client));
    var pending = cache.PreloadAsync();
    var truckUrl = $"api/fleet/trucks/{truckA}/planning";
    var dispatchUrl = $"api/dispatch/{oldA.DispatchId}/planning/automatic";
    if (truckChanged)
      cache.Store(truckUrl, oldA with { DispatchId = null, State = null });
    if (dispatchChanged)
      cache.Store(dispatchUrl, oldA with { State = null });
    var unrelated = Result(unrelatedTruck);
    var unrelatedUrl = $"api/fleet/trucks/{unrelatedTruck}/planning";
    cache.Store(unrelatedUrl, unrelated);
    handler.Release.SetResult();
    await pending;
    Assert.Equal(truckChanged, cache.Get(truckUrl) is null);
    Assert.Equal(dispatchChanged, cache.Get(dispatchUrl) is null);
    Assert.Equal(
      oldB.DispatchId,
      cache.Get($"api/fleet/trucks/{truckB}/planning")!.DispatchId
    );
    Assert.NotNull(
      cache.Get($"api/dispatch/{oldB.DispatchId}/planning/automatic")
    );
    Assert.Same(unrelated, cache.Get(unrelatedUrl));
  }

  [Fact]
  public async Task PreloadWriteTrackingIsBoundedAndReleasedBeforeTheNextRead()
  {
    var truck = Guid.NewGuid();
    var result = new AutomaticPlanningResult(
      truck,
      Guid.NewGuid(),
      1,
      new(
        new(),
        new() { Id = Guid.NewGuid(), Version = 1 },
        null,
        null,
        null,
        true
      ),
      null
    );
    using var handler = new PreviewHandler(result);
    using var client = new HttpClient(handler)
    {
      BaseAddress = new("https://local.test/"),
    };
    var cache = new PlanningDisplayCache(new ApiService(client));
    var pending = cache.PreloadAsync();
    for (var i = 0; i < 257; i++)
      cache.Store($"api/fleet/trucks/{Guid.NewGuid()}/planning", null);
    handler.Release.SetResult();
    await pending;
    var url = $"api/fleet/trucks/{truck}/planning";
    Assert.Null(cache.Get(url));
    await cache.PreloadAsync();
    Assert.NotNull(cache.Get(url));
    Assert.Equal(2, handler.Calls);
  }

  private sealed class PreviewHandler(params AutomaticPlanningResult[] results)
    : HttpMessageHandler
  {
    public int Calls;
    public TaskCompletionSource Release = new(
      TaskCreationOptions.RunContinuationsAsynchronously
    );

    protected override async Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request,
      CancellationToken cancellationToken
    )
    {
      Calls++;
      await Release.Task.WaitAsync(cancellationToken);
      return new(HttpStatusCode.OK)
      {
        Content = JsonContent.Create(
          new RequestResponseDTO<List<AutomaticPlanningResult>>
          {
            Success = true,
            Response = results.ToList(),
          }
        ),
      };
    }
  }

  [Fact]
  public async Task ConcurrentReadersShareRequestAndCancellationDoesNotCancelOtherReader()
  {
    using var handler = new DelayedHandler();
    using var client = new HttpClient(handler)
    {
      BaseAddress = new("https://local.test/"),
    };
    var cache = new PlanningDisplayCache(new ApiService(client));
    using var cancelled = new CancellationTokenSource();
    var first = cache.RefreshAsync("planning", cancelled.Token);
    var second = cache.RefreshAsync("planning", default);
    cancelled.Cancel();
    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
    handler.Release.SetResult();
    Assert.True((await second).Success);
    Assert.Equal(1, handler.Calls);
  }

  private sealed class DelayedHandler : HttpMessageHandler
  {
    public int Calls;
    public TaskCompletionSource Cancelled = new(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    public TaskCompletionSource Release = new(
      TaskCreationOptions.RunContinuationsAsynchronously
    );

    protected override async Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request,
      CancellationToken cancellationToken
    )
    {
      Calls++;
      try
      {
        await Release.Task.WaitAsync(cancellationToken);
      }
      catch (OperationCanceledException)
      {
        Cancelled.TrySetResult();
        throw;
      }
      return new(HttpStatusCode.OK)
      {
        Content = JsonContent.Create(
          new RequestResponseDTO<AutomaticPlanningResult>
          {
            Success = true,
            Response = new(
              Guid.NewGuid(),
              Guid.NewGuid(),
              1,
              new(new(), new() { Id = Guid.NewGuid() }, null, null, null, true),
              null
            ),
          }
        ),
      };
    }
  }

  [Fact]
  public async Task LastCancelledConsumerStopsTransportAndDoesNotBlockAReplacementRead()
  {
    using var handler = new DelayedHandler();
    using var client = new HttpClient(handler)
    {
      BaseAddress = new("https://local.test/"),
    };
    var cache = new PlanningDisplayCache(new ApiService(client));
    using var cancellation = new CancellationTokenSource();
    var read = cache.RefreshAsync("planning", cancellation.Token);
    cancellation.Cancel();
    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
    await handler.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Assert.Null(cache.Get("planning"));
    handler.Release.SetResult();
    Assert.True((await cache.RefreshAsync("planning", default)).Success);
    Assert.Equal(2, handler.Calls);
  }

  [Fact]
  public async Task ClearDuringRefreshDoesNotRestorePreviousSessionData()
  {
    using var handler = new DelayedHandler();
    using var client = new HttpClient(handler)
    {
      BaseAddress = new("https://local.test/"),
    };
    var cache = new PlanningDisplayCache(new ApiService(client));
    var running = cache.RefreshAsync("planning", default);
    cache.Clear();
    await handler.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Assert.False((await running).Success);
    Assert.Null(cache.Get("planning"));
    handler.Release.SetResult();
    Assert.True((await cache.RefreshAsync("planning", default)).Success);
    Assert.Equal(2, handler.Calls);
    Assert.NotNull(cache.Get("planning"));
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task RefreshRestoresOnlyMatchingGeometryAndKeepsFreshMetadata(
    bool changed
  )
  {
    var id = Guid.NewGuid();
    var previous = new RoutePlan
    {
      Id = id,
      Version = 1,
      Route = new() { Legs = [new(10, 600, [new(40, -80), new(41, -80)])] },
    };
    var next = new RoutePlan
    {
      Id = id,
      Version = changed ? 2 : 1,
      GeometryOmitted = !changed,
      Route = new()
      {
        Legs = [new(11, 650, changed ? [new(42, -80), new(43, -80)] : [])],
      },
      Stops = [new(Guid.NewGuid(), "Updated stop", "Address", 1, new(41, -80))],
    };
    AutomaticPlanningResult Result(RoutePlan plan) =>
      new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        1,
        new(new(), plan, null, 50, null, true),
        null
      );
    using var handler = new Handler(Result(next));
    using var client = new HttpClient(handler)
    {
      BaseAddress = new("https://local.test/"),
    };
    var cache = new PlanningDisplayCache(new ApiService(client));
    const string url = "api/fleet/trucks/test/planning";
    cache.Store(url, Result(previous));
    var response = await cache.RefreshAsync(url, default);
    Assert.Contains($"knownPlanId={id}&knownVersion=1", handler.Url);
    var plan = response.Response!.State!.Plan!;
    Assert.False(plan.GeometryOmitted);
    Assert.Equal(changed ? 42 : 40, plan.Route.Legs[0].Points[0].Latitude);
    Assert.Equal(11, plan.Route.Legs[0].Miles);
    Assert.Equal("Updated stop", Assert.Single(plan.Stops).Name);
    Assert.Same(response.Response, cache.Get(url));
    Assert.Empty(previous.Stops);
  }

  [Fact]
  public async Task RefreshRestoresEncodedGeometryForBothRoads()
  {
    var id = Guid.NewGuid();
    RoutePlan Plan(bool geometry) =>
      new()
      {
        Id = id,
        Version = 1,
        GeometryOmitted = !geometry,
        FromCurrentPosition = true,
        Route = new()
        {
          Legs = [new(10, 600, []) { Path = geometry ? "route" : null }],
        },
        ReferenceRoute = new()
        {
          Legs = [new(20, 900, []) { Path = geometry ? "behind" : null }],
        },
      };
    AutomaticPlanningResult Result(RoutePlan plan) =>
      new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        1,
        new(new(), plan, null, 50, null, true),
        null
      );
    using var handler = new Handler(Result(Plan(false)));
    using var client = new HttpClient(handler)
    {
      BaseAddress = new("https://local.test/"),
    };
    var cache = new PlanningDisplayCache(new ApiService(client));
    const string url = "api/fleet/trucks/test/planning";
    cache.Store(url, Result(Plan(true)));

    var plan = (await cache.RefreshAsync(url, default)).Response!.State!.Plan!;

    Assert.False(plan.GeometryOmitted);
    Assert.Equal("route", plan.Route.Legs[0].Path);
    Assert.Equal("behind", plan.ReferenceRoute!.Legs[0].Path);
    Assert.True(plan.Route.Legs[0].HasGeometry);
  }

  [Fact]
  public void APathIsSentOnToTheMapAndAnAbsentOneIsNotWritten()
  {
    var encoded = JsonSerializer.Serialize(
      new RouteLeg(1, 2, []) { Path = "abc" },
      JsonSerializerOptions.Web
    );
    Assert.Contains("\"path\":\"abc\"", encoded);
    Assert.DoesNotContain("hasGeometry", encoded);
    Assert.DoesNotContain(
      "path",
      JsonSerializer.Serialize(
        new RouteLeg(1, 2, [new(1, 2), new(3, 4)]),
        JsonSerializerOptions.Web
      )
    );
    Assert.False(new RouteLeg(1, 2, [new(1, 2)]).HasGeometry);
  }

  private sealed class Handler(AutomaticPlanningResult result)
    : HttpMessageHandler
  {
    public string Url = "";

    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request,
      CancellationToken cancellationToken
    )
    {
      Url = request.RequestUri!.ToString();
      return Task.FromResult(
        new HttpResponseMessage(HttpStatusCode.OK)
        {
          Content = JsonContent.Create(
            new RequestResponseDTO<AutomaticPlanningResult>
            {
              Success = true,
              Response = result,
            }
          ),
        }
      );
    }
  }
}
