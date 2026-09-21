using System.Diagnostics.Metrics;
using System.Net;
using Domain.Models.Routing;
using Domain.Rules;
using Microsoft.EntityFrameworkCore;
using Server.Tests.Support;
using Xunit.Abstractions;

namespace Server.Tests.Routing;

[CollectionDefinition("TomTom concurrency", DisableParallelization = true)]
public sealed class TomTomConcurrencyCollection;

[Collection("TomTom concurrency")]
[Trait("Category", "Routing")]
[Trait("Kind", "Integration")]
public sealed class TomTomConcurrencyTests(ITestOutputHelper output)
{
  private static TaskCompletionSource Signal() =>
    new(TaskCreationOptions.RunContinuationsAsynchronously);

  private static HttpResponseMessage Response() =>
    new(HttpStatusCode.OK)
    {
      Content = new StringContent(
        """{"routes":[{"summary":{"lengthInMeters":1000,"travelTimeInSeconds":60},"legs":[{"summary":{"lengthInMeters":1000,"travelTimeInSeconds":60},"points":[{"latitude":40,"longitude":-80},{"latitude":41,"longitude":-80}]}]}]}"""
      ),
    };

  private static RoutePoint[] Points(double latitude) =>
    [new(40, -80), new(latitude, -80)];

  [Fact]
  public async Task TwoDistinctBodiesOverlapWhileQueuedCancellationConsumesNoReservation()
  {
    using var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    using var thirdCancellation =
      CancellationTokenSource.CreateLinkedTokenSource(watchdog.Token);
    var firstEntered = Signal();
    var secondEntered = Signal();
    var thirdQueued = Signal();
    var release = Signal();
    var entered = 0;
    var queued = 0;
    using var listener = new MeterListener();
    listener.InstrumentPublished = (instrument, meter) =>
    {
      if (
        instrument.Meter.Name == "PulsarTms.Performance"
        && instrument.Name == "pulsartms.stage.items"
      )
        meter.EnableMeasurementEvents(instrument);
    };
    listener.SetMeasurementEventCallback<long>(
      (_, _, tags, _) =>
      {
        foreach (var tag in tags)
          if (
            tag.Key == "stage"
            && Equals(tag.Value, "provider-attempts-queued")
            && Interlocked.Increment(ref queued) == 3
          )
            thirdQueued.TrySetResult();
      }
    );
    listener.Start();
    await using var fixture = await TomTomProviderFixture.CreateAsync(
      async (_, ct) =>
      {
        var number = Interlocked.Increment(ref entered);
        if (number == 1)
          firstEntered.TrySetResult();
        if (number == 2)
          secondEntered.TrySetResult();
        await release.Task.WaitAsync(ct);
        return Response();
      },
      dailyRequestLimit: 10
    );
    await using var secondDb = fixture.CreateObserver();
    await using var thirdDb = fixture.CreateObserver();
    var first = fixture.CalculateAsync(41, watchdog.Token);
    Task<TruckRoute>? second = null;
    Task<TruckRoute>? third = null;
    try
    {
      await firstEntered.Task.WaitAsync(watchdog.Token);
      second = fixture
        .CreateProvider(secondDb)
        .CalculateAsync(
          Points(42),
          new() { UsesFleetDefaults = true },
          watchdog.Token
        );
      await secondEntered.Task.WaitAsync(watchdog.Token);
      third = fixture
        .CreateProvider(thirdDb)
        .CalculateAsync(
          Points(43),
          new() { UsesFleetDefaults = true },
          thirdCancellation.Token
        );
      await thirdQueued.Task.WaitAsync(watchdog.Token);
      Assert.Equal(2, fixture.Calls);
      await thirdCancellation.CancelAsync();
      await Assert.ThrowsAnyAsync<OperationCanceledException>(() => third);
      await using var observer = fixture.CreateObserver();
      Assert.Equal(
        2,
        await observer.RoutingApiCalls.CountAsync(watchdog.Token)
      );
    }
    finally
    {
      release.TrySetResult();
      await first;
      if (second is not null)
        await second;
    }
    Assert.Equal(2, fixture.Calls);
    Assert.Equal(
      2,
      await fixture.Db.RoutingApiCalls.CountAsync(watchdog.Token)
    );
    output.WriteLine(
      "Controlled provider fixture: two unrelated response bodies entered before either was released; a third cancelled waiter issued zero HTTP calls and zero reservations. This proves overlap and the two-call bound, not a production latency gain."
    );
  }

  [Fact]
  public async Task SameRequestWaitsForItsOwnerAndReusesThePersistedResult()
  {
    using var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    var entered = Signal();
    var release = Signal();
    await using var fixture = await TomTomProviderFixture.CreateAsync(
      async (_, ct) =>
      {
        entered.TrySetResult();
        await release.Task.WaitAsync(ct);
        return Response();
      }
    );
    await using var peerDb = fixture.CreateObserver();
    var first = fixture.CalculateAsync(ct: watchdog.Token);
    Task<TruckRoute>? peer = null;
    try
    {
      await entered.Task.WaitAsync(watchdog.Token);
      peer = fixture
        .CreateProvider(peerDb)
        .CalculateAsync(
          Points(41),
          new() { UsesFleetDefaults = true },
          watchdog.Token
        );
      Assert.False(peer.IsCompleted);
    }
    finally
    {
      release.TrySetResult();
    }
    var route = await first;
    var reused = await peer!;
    Assert.NotSame(route, reused);
    Assert.Equal(route.Legs[0].Points, reused.Legs[0].Points);
    Assert.Equal(1, fixture.Calls);
    Assert.Equal(
      1,
      await fixture.Db.RoutingApiCalls.CountAsync(watchdog.Token)
    );
  }

  [Fact]
  public async Task CancellingSameRequestWaiterDoesNotCancelTheOwnerOrSpendQuota()
  {
    using var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
      watchdog.Token
    );
    var entered = Signal();
    var release = Signal();
    await using var fixture = await TomTomProviderFixture.CreateAsync(
      async (_, ct) =>
      {
        entered.TrySetResult();
        await release.Task.WaitAsync(ct);
        return Response();
      }
    );
    await using var peerDb = fixture.CreateObserver();
    var first = fixture.CalculateAsync(ct: watchdog.Token);
    try
    {
      await entered.Task.WaitAsync(watchdog.Token);
      var peer = fixture
        .CreateProvider(peerDb)
        .CalculateAsync(
          Points(41),
          new() { UsesFleetDefaults = true },
          cancellation.Token
        );
      await cancellation.CancelAsync();
      await Assert.ThrowsAnyAsync<OperationCanceledException>(() => peer);
      Assert.False(first.IsCompleted);
    }
    finally
    {
      release.TrySetResult();
    }
    Assert.Equal(2, (await first).Points.Count);
    Assert.Equal(1, fixture.Calls);
    Assert.Equal(
      1,
      await fixture.Db.RoutingApiCalls.CountAsync(watchdog.Token)
    );
  }

  [Fact]
  public async Task ConcurrentDistinctRequestStillHonorsCommittedDailyBudget()
  {
    using var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    var entered = Signal();
    var release = Signal();
    await using var fixture = await TomTomProviderFixture.CreateAsync(
      async (_, ct) =>
      {
        entered.TrySetResult();
        await release.Task.WaitAsync(ct);
        return Response();
      },
      dailyRequestLimit: 1
    );
    await using var peerDb = fixture.CreateObserver();
    var first = fixture.CalculateAsync(ct: watchdog.Token);
    try
    {
      await entered.Task.WaitAsync(watchdog.Token);
      var error = await Assert.ThrowsAsync<RoutePlanningException>(
        () =>
          fixture
            .CreateProvider(peerDb)
            .CalculateAsync(
              Points(42),
              new() { UsesFleetDefaults = true },
              watchdog.Token
            )
      );
      Assert.Contains("daily TomTom request limit", error.Message);
      Assert.Equal(1, fixture.Calls);
    }
    finally
    {
      release.TrySetResult();
    }
    await first;
    Assert.Equal(
      1,
      await fixture.Db.RoutingApiCalls.CountAsync(watchdog.Token)
    );
  }
}
