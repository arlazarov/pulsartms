using Infrastructure.Integrations.Samsara;
using Server.Tests.Support;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
namespace Server.Tests.Fleet;
[Trait("Category", "Fleet")]
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
  private static SamsaraDriverHosProvider Provider(Handler handler, IMemoryCache cache)
  {
    return new(new SamsaraApiService(new HttpClient(handler), new StubProviderCredentials(("apiKey", "test"))), cache, NullLogger<SamsaraDriverHosProvider>.Instance);
  }
  private class Handler : HttpMessageHandler
  {
    public int Count;
    public HttpStatusCode Status = HttpStatusCode.OK;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
      Count++;
      return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent("""{"data":[{"driver":{"id":"123"},"currentDutyStatus":{"hosStatusType":"sleeperBed"},"clocks":{"drive":{"driveRemainingDurationMs":3600000},"break":{"timeUntilBreakDurationMs":0}}}]}""") });
    }
  }
}
