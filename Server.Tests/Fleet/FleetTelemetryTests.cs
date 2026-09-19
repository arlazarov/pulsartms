using Application.Features.Fleet.Interfaces;
using Application.Features.Fleet.Models;
using Application.Features.Fleet.Queries.GetFleetLocations;
using Application.Features.Synchronization.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Server.Tests.Fleet;

[Trait("Category", "Fleet")]
[Trait("Kind", "Unit")]
public class FleetTelemetryTests
{
  [Fact]
  public async Task StreamReadsAllPagesWithTheSameStartTime()
  {
    var provider = new Provider();
    using var stream = new FleetLocationStream(TimeProvider.System);
    var result = await stream.GetAsync(
      provider,
      [new FleetTruckInfo { TruckExternalId = "truck", IsActive = true }]
    );
    Assert.Equal(2, result.Count);
    Assert.Equal(new string?[] { null, "next" }, provider.Cursors);
    Assert.Single(provider.StartTimes.Distinct());
    Assert.Single(provider.EndTimes.Distinct());
    Assert.Equal(
      TimeSpan.FromSeconds(60),
      provider.EndTimes[0] - provider.StartTimes[0]
    );
  }

  [Fact]
  public async Task RepeatedCursorFailsInsteadOfReturningPartialData()
  {
    using var stream = new FleetLocationStream(TimeProvider.System);
    await Assert.ThrowsAsync<InvalidOperationException>(
      () =>
        stream.GetAsync(
          new Provider { RepeatCursor = true },
          [new FleetTruckInfo { TruckExternalId = "truck", IsActive = true }]
        )
    );
  }

  [Fact]
  public async Task ConcurrentClientsShareOneLoadAndFailuresCanRetry()
  {
    using var memory = new MemoryCache(new MemoryCacheOptions());
    using var cache = new FleetTelemetryCache(memory);
    await Assert.ThrowsAsync<HttpRequestException>(
      () =>
        cache.GetAsync(
          _ =>
            Task.FromException<FleetLocationsResponse>(
              new HttpRequestException()
            ),
          default
        )
    );
    var release = new TaskCompletionSource<FleetLocationsResponse>(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    var calls = 0;
    Task<FleetLocationsResponse> Load(CancellationToken token)
    {
      Interlocked.Increment(ref calls);
      return release.Task;
    }
    var requests = Enumerable
      .Range(0, 10)
      .Select(_ => cache.GetAsync(Load, default))
      .ToArray();
    var response = new FleetLocationsResponse();
    release.SetResult(response);
    var results = await Task.WhenAll(requests);
    Assert.Equal(1, calls);
    Assert.All(results, value => Assert.Same(response, value));
    memory.Remove(FleetTelemetryCache.CacheKey);
    await cache.GetAsync(Load, default);
    Assert.Equal(2, calls);
  }

  [Fact]
  public async Task ACancelledReaderDoesNotReceiveAWarmTelemetrySnapshot()
  {
    using var memory = new MemoryCache(new MemoryCacheOptions());
    using var cache = new FleetTelemetryCache(memory);
    await cache.GetAsync(
      _ => Task.FromResult(new FleetLocationsResponse()),
      default
    );
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();
    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      () =>
        cache.GetAsync(
          _ => throw new InvalidOperationException(),
          cancellation.Token
        )
    );
  }

  [Fact]
  public async Task IncrementalWindowsRetainOldPointsReplaceCorrectionsAndDoNotShareMutableMetadata()
  {
    var clock = new ManualTimeProvider();
    var initial = clock.GetUtcNow().UtcDateTime;
    using var stream = new FleetLocationStream(clock);
    var truck = new FleetTruckInfo
    {
      TruckId = Guid.NewGuid(),
      TruckExternalId = "truck",
      IsActive = true,
      UnitNumber = "First",
    };
    var oldPoint = new VehicleLocationPoint
    {
      ExternalId = "truck",
      UpdatedAt = initial.AddSeconds(-50),
      Latitude = 40,
    };
    var corrected = new VehicleLocationPoint
    {
      ExternalId = "truck",
      UpdatedAt = initial.AddSeconds(-5),
      Latitude = 41,
    };
    var provider = new StubFleetTelemetryProvider
    {
      Read = (_, _) =>
        Task.FromResult(
          new VehicleLocationStream { Data = [oldPoint, corrected] }
        ),
    };
    var first = await stream.GetAsync(provider, [truck]);
    first[0].Latitude = 88;
    first[0].DriverName = "Changed by caller";
    oldPoint.Latitude = 89;
    corrected.Latitude = 42;
    corrected.ExternalId = "TRUCK";
    truck.UnitNumber = "Second";
    clock.Advance(TimeSpan.FromSeconds(10));
    provider.Read = (_, _) =>
      Task.FromResult(new VehicleLocationStream { Data = [corrected] });
    var second = await stream.GetAsync(provider, [truck]);
    Assert.Equal(initial.AddSeconds(-10), provider.Requests[^1].From);
    Assert.Equal(2, second.Count);
    Assert.Equal(40, second[0].Latitude);
    Assert.Equal(42, second[1].Latitude);
    Assert.All(second, point => Assert.Equal("Second", point.UnitNumber));
    Assert.All(second, point => Assert.Equal("", point.DriverName));
  }

  [Fact]
  public async Task PeriodicFullWindowRecoversOlderLatePointsAndPrunesExpiredOnes()
  {
    var clock = new ManualTimeProvider();
    var initial = clock.GetUtcNow().UtcDateTime;
    using var stream = new FleetLocationStream(clock);
    var fleet = new[]
    {
      new FleetTruckInfo { TruckExternalId = "truck", IsActive = true },
    };
    var provider = new StubFleetTelemetryProvider
    {
      Read = (_, _) =>
        Task.FromResult(
          new VehicleLocationStream
          {
            Data =
            [
              new()
              {
                ExternalId = "truck",
                UpdatedAt = initial.AddSeconds(-50),
              },
            ],
          }
        ),
    };
    await stream.GetAsync(provider, fleet);
    clock.Advance(TimeSpan.FromSeconds(10));
    provider.Read = (_, _) => Task.FromResult(new VehicleLocationStream());
    await stream.GetAsync(provider, fleet);
    clock.Advance(TimeSpan.FromSeconds(20));
    provider.Read = (_, _) =>
      Task.FromResult(
        new VehicleLocationStream
        {
          Data =
          [
            new() { ExternalId = "truck", UpdatedAt = initial.AddSeconds(-20) },
          ],
        }
      );
    var result = await stream.GetAsync(provider, fleet);
    Assert.Equal(initial.AddSeconds(-30), provider.Requests[^1].From);
    Assert.Equal(initial.AddSeconds(-20), Assert.Single(result).UpdatedAt);
  }

  [Fact]
  public async Task FullReconciliationRemovesPointsNoLongerPresentInTheProviderWindow()
  {
    var clock = new ManualTimeProvider();
    using var stream = new FleetLocationStream(clock);
    var fleet = new[]
    {
      new FleetTruckInfo { TruckExternalId = "truck", IsActive = true },
    };
    var provider = new StubFleetTelemetryProvider
    {
      Read = (request, _) =>
        Task.FromResult(
          new VehicleLocationStream
          {
            Data = [new() { ExternalId = "truck", UpdatedAt = request.To }],
          }
        ),
    };
    Assert.Single(await stream.GetAsync(provider, fleet));
    clock.Advance(TimeSpan.FromSeconds(30));
    provider.Read = (_, _) => Task.FromResult(new VehicleLocationStream());
    Assert.Empty(await stream.GetAsync(provider, fleet));
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task FailedOrCancelledPaginationDoesNotAdvanceTheWatermark(
    bool cancel
  )
  {
    var clock = new ManualTimeProvider();
    var initial = clock.GetUtcNow().UtcDateTime;
    using var stream = new FleetLocationStream(clock);
    var fleet = new[]
    {
      new FleetTruckInfo { TruckExternalId = "truck", IsActive = true },
    };
    var provider = new StubFleetTelemetryProvider();
    await stream.GetAsync(provider, fleet);
    clock.Advance(TimeSpan.FromSeconds(10));
    using var cancellation = new CancellationTokenSource();
    provider.Read = (request, ct) =>
    {
      if (request.Cursor is null)
        return Task.FromResult(
          new VehicleLocationStream { HasNextPage = true, EndCursor = "next" }
        );
      if (cancel)
      {
        cancellation.Cancel();
        ct.ThrowIfCancellationRequested();
      }
      throw new HttpRequestException("Test failure");
    };
    if (cancel)
      await Assert.ThrowsAnyAsync<OperationCanceledException>(
        () => stream.GetAsync(provider, fleet, cancellation.Token)
      );
    else
      await Assert.ThrowsAsync<HttpRequestException>(
        () => stream.GetAsync(provider, fleet)
      );
    clock.Advance(TimeSpan.FromSeconds(5));
    provider.Read = (_, _) => Task.FromResult(new VehicleLocationStream());
    await stream.GetAsync(provider, fleet);
    Assert.Equal(initial.AddSeconds(-10), provider.Requests[^1].From);
  }

  [Theory]
  [InlineData("fleet")]
  [InlineData("gap")]
  [InlineData("clock")]
  public async Task FleetChangesGapsAndClockRollbackBootstrapAFullWindow(
    string change
  )
  {
    var clock = new ManualTimeProvider();
    using var stream = new FleetLocationStream(clock);
    var truck = new FleetTruckInfo
    {
      TruckExternalId = "truck",
      IsActive = true,
    };
    var provider = new StubFleetTelemetryProvider();
    await stream.GetAsync(provider, [truck]);
    clock.Advance(
      change == "gap" ? TimeSpan.FromMinutes(2)
      : change == "clock" ? TimeSpan.FromSeconds(-5)
      : TimeSpan.FromSeconds(10)
    );
    if (change == "fleet")
      truck.TruckExternalId = "another";
    await stream.GetAsync(provider, [truck]);
    Assert.Equal(
      TimeSpan.FromSeconds(60),
      provider.Requests[^1].To - provider.Requests[^1].From
    );
  }

  [Fact]
  public async Task OversizedWindowFailsWithoutAcknowledgingAnIncompleteIncrementalBaseline()
  {
    var clock = new ManualTimeProvider();
    using var stream = new FleetLocationStream(clock);
    var fleet = new[]
    {
      new FleetTruckInfo { TruckExternalId = "truck", IsActive = true },
    };
    var provider = new StubFleetTelemetryProvider
    {
      Read = (request, _) =>
        Task.FromResult(
          new VehicleLocationStream
          {
            Data = Enumerable
              .Range(0, 65537)
              .Select(i => new VehicleLocationPoint
              {
                ExternalId = "truck",
                UpdatedAt = request.To.AddTicks(-i),
              })
              .ToArray(),
          }
        ),
    };
    await Assert.ThrowsAsync<InvalidOperationException>(
      () => stream.GetAsync(provider, fleet)
    );
    clock.Advance(TimeSpan.FromSeconds(10));
    provider.Read = (_, _) => Task.FromResult(new VehicleLocationStream());
    Assert.Empty(await stream.GetAsync(provider, fleet));
    Assert.Equal(
      TimeSpan.FromSeconds(60),
      provider.Requests[^1].To - provider.Requests[^1].From
    );
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task FallbackHonorsHighFrequencyFlagAndStillRefreshesFleetStats(
    bool highFrequency
  )
  {
    using var memory = new MemoryCache(new MemoryCacheOptions());
    var truckId = Guid.NewGuid();
    memory.Set(
      FleetCache.CacheKey,
      new FleetTruckInfo[]
      {
        new()
        {
          TruckId = truckId,
          TruckExternalId = "truck",
          IsActive = true,
        },
      }
    );
    var now = DateTime.UtcNow;
    var provider = new StubFleetTelemetryProvider
    {
      Vehicles =
      [
        new()
        {
          ExternalId = "truck",
          UpdatedAt = now.AddSeconds(-10),
          Latitude = 35,
          FormattedLocation = "I 40, Los Pinos, NM",
          FuelPercent = 36,
        },
      ],
      Read = (_, _) =>
        Task.FromResult(
          new VehicleLocationStream
          {
            Data =
            [
              new()
              {
                ExternalId = "truck",
                UpdatedAt = now,
                Latitude = 35.1m,
                FormattedLocation = "I 40, Los Pinos, NM 87026, US",
              },
            ],
          }
        ),
    };
    using var stream = new FleetLocationStream(TimeProvider.System);
    using var telemetry = new FleetTelemetryCache(memory);
    var handler = new GetFleetLocationsHandler(
      null!,
      provider,
      new(memory),
      telemetry,
      stream,
      new(),
      Options.Create(
        new SynchronizationOptions
        {
          Enabled = false,
          HighFrequencyLocations = highFrequency,
        }
      )
    );
    var result = await handler.Handle(new(), default);
    var truck = Assert.Single(result.Response!.Trucks);
    Assert.Equal(truckId, truck.TruckId);
    Assert.Equal(
      highFrequency ? "I 40, Los Pinos, NM 87026, US" : "I 40, Los Pinos, NM",
      truck.FormattedLocation
    );
    Assert.Equal(highFrequency ? now : now.AddSeconds(-10), truck.UpdatedAt);
    Assert.Equal(highFrequency ? 35.1m : 35, truck.Latitude);
    Assert.Equal(36, truck.FuelPercent);
    Assert.Equal(1, provider.StatsCalls);
    Assert.Equal(highFrequency ? 1 : 0, provider.Requests.Count);
    await handler.Handle(new(), default);
    Assert.Equal(1, provider.StatsCalls);
  }

  private sealed class Provider : IFleetTelemetryProvider
  {
    public bool RepeatCursor { get; init; }
    public List<string?> Cursors { get; } = [];
    public List<DateTime> StartTimes { get; } = [];
    public List<DateTime> EndTimes { get; } = [];

    public Task<IReadOnlyList<VehicleTelemetry>> GetVehicleTelemetryAsync(
      CancellationToken cancellationToken = default
    ) => Task.FromResult<IReadOnlyList<VehicleTelemetry>>([]);

    public Task<VehicleLocationStream> GetLocationStreamAsync(
      IReadOnlyCollection<string> vehicleIds,
      DateTime startTime,
      DateTime endTime,
      string? cursor = null,
      CancellationToken cancellationToken = default
    )
    {
      Cursors.Add(cursor);
      StartTimes.Add(startTime);
      EndTimes.Add(endTime);
      return Task.FromResult(
        new VehicleLocationStream
        {
          Data =
          [
            new VehicleLocationPoint
            {
              ExternalId = "truck",
              UpdatedAt = startTime.AddSeconds(Cursors.Count),
            },
          ],
          HasNextPage = cursor is null || RepeatCursor,
          EndCursor = "next",
        }
      );
    }
  }
}
