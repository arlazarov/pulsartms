using System.Net;
using Application.Features.Fleet.Background;
using Application.Features.Fleet.Interfaces;
using Application.Features.Fleet.Services;
using Application.Features.Synchronization.Interfaces;
using Application.Features.Synchronization.Models;
using Application.Features.Synchronization.Options;
using Domain.Models.Fleet;
using Infrastructure.Integrations.Samsara;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Server.Tests.Support;

namespace Server.Tests.Fleet;

[Trait("Category", "Fleet")]
[Trait("Kind", "Integration")]
public class DriverHosTests
{
  [Fact]
  public async Task MapsActualClocksAndCachesAllDrivers()
  {
    var handler = new Handler();
    using var cache = new MemoryCache(new MemoryCacheOptions());
    var provider = Provider(handler, cache);
    var clocks = await provider.GetClocksAsync(default);
    Assert.Equal(3600000L, clocks["123"].DriveMs);
    Assert.Equal(0L, clocks["123"].BreakMs);
    Assert.Equal("sleeperBerth", clocks["123"].CurrentDutyStatus);
    Assert.Null(clocks["123"].CycleMs);
    Assert.True(clocks["123"].UpdatedAt > DateTime.UtcNow.AddMinutes(-1));
    await provider.GetClocksAsync(default);
    Assert.Equal(1, handler.Count);
  }

  [Fact]
  public async Task MissingPermissionDoesNotBreakDispatchBoard()
  {
    var handler = new Handler { Status = HttpStatusCode.Forbidden };
    using var cache = new MemoryCache(new MemoryCacheOptions());
    Assert.Empty(await Provider(handler, cache).GetClocksAsync(default));
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
    using var cache = new MemoryCache(new MemoryCacheOptions());
    var providerLogger = new CaptureLogger<SamsaraDriverHosProvider>();
    var provider = new SamsaraDriverHosProvider(
      new SamsaraApiService(
        http,
        new StubProviderCredentials(("apiKey", "test"))
      ),
      cache,
      providerLogger
    );
    await using var services = new ServiceCollection()
      .AddSingleton<IDriverHosRefreshProvider>(provider)
      .BuildServiceProvider();
    var operationLogger = new CaptureLogger<DriverHosRefreshOperation>();
    var operation = new DriverHosRefreshOperation(
      snapshot,
      services.GetRequiredService<IServiceScopeFactory>(),
      Options.Create(new SynchronizationOptions { Enabled = true }),
      new ActiveSynchronization(),
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
    Assert.Empty(providerLogger.Messages);
    time.Advance(TimeSpan.FromSeconds(15));
    Assert.Empty(await snapshot.GetClocksAsync(default));
    await operation.RunOnceAsync(default);
    Assert.Equal(1, handler.Count);
  }

  private sealed class ActiveSynchronization : ISynchronizationStatusProvider
  {
    public SynchronizationStatus Status => new(true, true, 0, []);
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

  private static SamsaraDriverHosProvider Provider(
    Handler handler,
    IMemoryCache cache
  )
  {
    return new(
      new SamsaraApiService(
        new HttpClient(handler),
        new StubProviderCredentials(("apiKey", "test"))
      ),
      cache,
      NullLogger<SamsaraDriverHosProvider>.Instance
    );
  }

  private class Handler : HttpMessageHandler
  {
    public int Count;
    public HttpStatusCode Status = HttpStatusCode.OK;
    public bool Timeout;
    public string Body =
      """{"data":[{"driver":{"id":"123"},"currentDutyStatus":{"hosStatusType":"sleeperBed"},"clocks":{"drive":{"driveRemainingDurationMs":3600000},"break":{"timeUntilBreakDurationMs":0}}}]}""";

    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request,
      CancellationToken ct
    )
    {
      Count++;
      if (Timeout)
        throw new OperationCanceledException();
      return Task.FromResult(
        new HttpResponseMessage(Status) { Content = new StringContent(Body) }
      );
    }
  }
}
