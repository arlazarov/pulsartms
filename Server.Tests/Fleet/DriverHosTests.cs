using System.Net;
using Application.Features.Fleet.Background;
using Application.Features.Fleet.Interfaces;
using Application.Features.Fleet.Services;
using Application.Features.Synchronization.Interfaces;
using Application.Features.Synchronization.Models;
using Application.Features.Synchronization.Options;
using Domain.Models.Fleet;
using Infrastructure.Integrations.Samsara;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Server.Tests.Support;

namespace Server.Tests.Fleet;

[Trait("Category", "Fleet")]
[Trait("Kind", "Integration")]
public class DriverHosTests
{
  // Audit D3: the provider keeps nothing. Each refresh asks the carrier's
  // own account; the snapshot keeps the clocks per carrier.
  [Fact]
  public async Task MapsActualClocksAndKeepsNothing()
  {
    var handler = new Handler();
    var provider = Provider(handler);
    var clocks = await provider.RefreshClocksAsync(default);
    Assert.Equal(3600000L, clocks["123"].DriveMs);
    Assert.Equal(0L, clocks["123"].BreakMs);
    Assert.Equal("sleeperBerth", clocks["123"].CurrentDutyStatus);
    Assert.Null(clocks["123"].CycleMs);
    Assert.True(clocks["123"].UpdatedAt > DateTime.UtcNow.AddMinutes(-1));
    await provider.RefreshClocksAsync(default);
    Assert.Equal(2, handler.Count);
  }

  // A missing permission fails the background refresh, which says so; the
  // board reads the snapshot and still opens.
  [Fact]
  public async Task MissingPermissionDoesNotBreakDispatchBoard()
  {
    var snapshot = new DriverHosSnapshot(
      TimeProvider.System,
      new TestCompany()
    );
    var logger = new CaptureLogger<DriverHosRefreshOperation>();
    await using var services = new ServiceCollection()
      .AddSingleton<IDriverHosRefreshProvider>(
        Provider(new Handler { Status = HttpStatusCode.Forbidden })
      )
      .BuildServiceProvider();

    await new DriverHosRefreshOperation(
      snapshot,
      services.GetRequiredService<IServiceScopeFactory>(),
      new TestFleetCollectionState(Enabled: true, Active: true),
      logger
    ).RunOnceAsync(default);

    Assert.Empty(await snapshot.GetClocksAsync(default));
    Assert.Single(logger.Messages);
  }

  // Two carriers' refreshes reach the provider at once: nothing makes one
  // carrier wait for another (the static gate D3 removed did).
  [Fact]
  public async Task TwoCarriersRefreshAtOnce()
  {
    var inside = 0;
    var most = 0;
    var both = new TaskCompletionSource(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    Handler Held() =>
      new()
      {
        During = async () =>
        {
          var now = Interlocked.Increment(ref inside);
          InterlockedMax(ref most, now);
          if (now == 2)
            both.TrySetResult();
          await Task.WhenAny(both.Task, Task.Delay(TimeSpan.FromSeconds(2)));
          Interlocked.Decrement(ref inside);
        },
      };

    await Task.WhenAll(
      Provider(Held()).RefreshClocksAsync(default),
      Provider(Held()).RefreshClocksAsync(default)
    );

    Assert.Equal(2, most);
  }

  [Theory]
  [InlineData(false, false)]
  [InlineData(true, false)]
  [InlineData(false, true)]
  public async Task BackgroundRefreshDistinguishesFailureFromEmptyWithoutExtendingClockFreshness(
    bool empty,
    bool timeout
  )
  {
    var time = new ManualTimeProvider();
    var snapshot = new DriverHosSnapshot(time, new TestCompany());
    var observed = time.GetUtcNow().UtcDateTime;
    snapshot.Complete(
      new Dictionary<string, DriverHosClocks>
      {
        ["123"] = new() { DriveMs = 42, UpdatedAt = observed },
      }
    );
    time.Advance(TimeSpan.FromSeconds(45));
    using var handler = new Handler
    {
      Status = empty ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable,
      Body = """{"data":[]}""",
      Timeout = timeout,
    };
    using var http = new HttpClient(handler);
    var provider = new SamsaraDriverHosProvider(
      new SamsaraApiService(
        http,
        new StubProviderCredentials(("apiKey", "test"))
      )
    );
    await using var services = new ServiceCollection()
      .AddSingleton<IDriverHosRefreshProvider>(provider)
      .BuildServiceProvider();
    var operationLogger = new CaptureLogger<DriverHosRefreshOperation>();
    var operation = new DriverHosRefreshOperation(
      snapshot,
      services.GetRequiredService<IServiceScopeFactory>(),
      new TestFleetCollectionState(Enabled: true, Active: true),
      operationLogger
    );

    await operation.RunOnceAsync(default);

    var remaining = await snapshot.GetClocksAsync(default);
    if (empty)
      Assert.Empty(remaining);
    else
    {
      Assert.Equal(42, remaining["123"].DriveMs);
      Assert.Equal(observed, remaining["123"].UpdatedAt);
      if (!timeout)
      {
        var log = Assert.Single(operationLogger.Messages);
        Assert.Contains("driver-hos", log);
        Assert.Contains("TraceId", log);
      }
    }
    Assert.Equal(empty || timeout ? 0 : 1, operationLogger.Messages.Count);
    time.Advance(TimeSpan.FromSeconds(15));
    Assert.Empty(await snapshot.GetClocksAsync(default));
    await operation.RunOnceAsync(default);
    Assert.Equal(1, handler.Count);
  }

  private sealed class CaptureLogger<T> : ILogger<T>
  {
    public List<string> Messages { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
      where TState : notnull => null;

    public bool IsEnabled(LogLevel level) => true;

    public void Log<TState>(
      LogLevel level,
      EventId id,
      TState state,
      Exception? exception,
      Func<TState, Exception?, string> formatter
    ) => Messages.Add(formatter(state, exception));
  }

  private static SamsaraDriverHosProvider Provider(Handler handler) =>
    new(
      new SamsaraApiService(
        new HttpClient(handler),
        new StubProviderCredentials(("apiKey", "test"))
      )
    );

  private static void InterlockedMax(ref int most, int value)
  {
    for (
      var seen = Volatile.Read(ref most);
      value > seen
        && Interlocked.CompareExchange(ref most, value, seen) != seen;
      seen = Volatile.Read(ref most)
    ) { }
  }

  private class Handler : HttpMessageHandler
  {
    public int Count;
    public HttpStatusCode Status = HttpStatusCode.OK;
    public bool Timeout;
    public Func<Task>? During;
    public string Body =
      """{"data":[{"driver":{"id":"123"},"currentDutyStatus":{"hosStatusType":"sleeperBed"},"clocks":{"drive":{"driveRemainingDurationMs":3600000},"break":{"timeUntilBreakDurationMs":0}}}]}""";

    protected override async Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request,
      CancellationToken ct
    )
    {
      Interlocked.Increment(ref Count);
      if (Timeout)
        throw new OperationCanceledException();
      if (During is { } during)
        await during();
      return new HttpResponseMessage(Status)
      {
        Content = new StringContent(Body),
      };
    }
  }
}
