using Application.Features.Fuel.Exceptions;
using Application.Features.Fuel.Interfaces;
using Application.Features.Fuel.Models;
using Application.Features.Fuel.Services;
using Server.Tests.Support;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Unit")]
public sealed class FuelStationLookupLifecycleTests
{
  [Fact]
  public async Task ExceptionsReserveBeforeDispatchAndBackOffAcrossServiceInstances()
  {
    var store = new MemoryFuelStationLookupStore();
    var clock = new Clock();
    var places = new Places();
    places.Response = async ct =>
    {
      var reserved = await store.ReadAsync("station", ct);
      Assert.NotNull(reserved);
      Assert.True(reserved.NextAttemptAt > clock.Now.UtcDateTime);
      throw new HttpRequestException("Provider unavailable");
    };
    foreach (var minutes in new[] { 15, 30, 60, 120, 240, 360, 360 })
    {
      await Assert.ThrowsAsync<HttpRequestException>(
        () =>
          new FuelStationLookupService(store, places, clock).FindAsync(
            "station",
            "query",
            default
          )
      );
      var state = await store.ReadAsync("station", default);
      Assert.Equal(
        clock.Now.UtcDateTime.AddMinutes(minutes),
        state!.NextAttemptAt
      );
      Assert.Equal(nameof(HttpRequestException), state.ErrorCode);
      var calls = places.Calls;
      await Assert.ThrowsAsync<FuelStationLookupDeferredException>(
        () =>
          new FuelStationLookupService(store, places, clock).FindAsync(
            "station",
            "QUERY",
            default
          )
      );
      Assert.Equal(calls, places.Calls);
      clock.Now = new(state.NextAttemptAt, TimeSpan.Zero);
    }
  }

  [Fact]
  public async Task CancellationKeepsReservationButDoesNotInventAProviderFailure()
  {
    var store = new MemoryFuelStationLookupStore();
    var clock = new Clock();
    using var cancellation = new CancellationTokenSource();
    var places = new Places
    {
      Response = _ =>
      {
        cancellation.Cancel();
        throw new OperationCanceledException(cancellation.Token);
      },
    };
    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      () =>
        new FuelStationLookupService(store, places, clock).FindAsync(
          "station",
          "query",
          cancellation.Token
        )
    );
    var state = await store.ReadAsync("station", default);
    Assert.Equal(clock.Now.UtcDateTime.AddMinutes(15), state!.NextAttemptAt);
    Assert.Null(state.ErrorCode);
    await Assert.ThrowsAsync<FuelStationLookupDeferredException>(
      () =>
        new FuelStationLookupService(store, places, clock).FindAsync(
          "station",
          "query",
          default
        )
    );
    Assert.Equal(1, places.Calls);
  }

  [Fact]
  public async Task ChangedQueriesBypassCooldownButNeverAnActiveOwner()
  {
    var store = new MemoryFuelStationLookupStore();
    var clock = new Clock();
    var entered = new TaskCompletionSource(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    var result = new TaskCompletionSource<PlaceSearchResult?>(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    var places = new Places
    {
      Response = _ =>
      {
        entered.SetResult();
        return result.Task;
      },
    };
    var first = new FuelStationLookupService(store, places, clock).FindAsync(
      "station",
      "old query",
      default
    );
    await entered.Task;
    await Assert.ThrowsAsync<FuelStationLookupDeferredException>(
      () =>
        new FuelStationLookupService(store, places, clock).FindAsync(
          "station",
          "correct query",
          default
        )
    );
    Assert.Equal(1, places.Calls);
    result.SetResult(null);
    await first;
    places.Response = _ =>
      Task.FromResult<PlaceSearchResult?>(
        new()
        {
          Address = "Corrected",
          Latitude = 40,
          Longitude = -80,
        }
      );
    var corrected = await new FuelStationLookupService(
      store,
      places,
      clock
    ).FindAsync("station", "correct query", default);
    Assert.Equal("Corrected", corrected!.Address);
    Assert.Equal(2, places.Calls);
    var reused = await new FuelStationLookupService(
      store,
      places,
      clock
    ).FindAsync("station", "correct query", default);
    Assert.Equal("Corrected", reused!.Address);
    Assert.Equal(2, places.Calls);
  }

  private sealed class Clock : TimeProvider
  {
    public DateTimeOffset Now = DateTimeOffset.UtcNow;

    public override DateTimeOffset GetUtcNow() => Now;
  }

  private sealed class Places : IPlaceSearchService
  {
    public int Calls;
    public Func<CancellationToken, Task<PlaceSearchResult?>> Response = _ =>
      Task.FromResult<PlaceSearchResult?>(null);

    public Task<PlaceSearchResult?> SearchAsync(
      string query,
      CancellationToken ct = default
    )
    {
      Calls++;
      return Response(ct);
    }
  }
}
