using System.Net;
using System.Net.Http.Json;
using Client.Models.DTO;
using Client.Models.DTO.Planning;
using Client.Services;
using Client.Tests.Support;
using Microsoft.Extensions.Time.Testing;

namespace Client.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Unit")]
public sealed class PlanningDisplayCacheBudgetTests
{
  [Fact]
  public void OversizedEntryIsNotRetainedAndDoesNotEvictUnrelatedRoutes()
  {
    using var fixture = new CacheFixture();
    var saved = Result(120_000);
    fixture.Cache.Store("saved", saved);
    fixture.Cache.Store("oversized", Result(300_000));

    Assert.Same(saved, fixture.Cache.Get("saved"));
    Assert.Null(fixture.Cache.Get("oversized"));
  }

  [Fact]
  public void MainLegAndReferenceGeometryAreAllCountedConservatively()
  {
    using var fixture = new CacheFixture();
    var result = Result(100_000);
    var plan = result.State!.Plan!;
    plan.Route.Legs = [new(10, 600, plan.Route.Points)];
    plan.ReferenceRoute = new() { Points = plan.Route.Points };
    fixture.Cache.Store("shared-geometry", result);

    Assert.Null(fixture.Cache.Get("shared-geometry"));
  }

  [Fact]
  public void EmptyLegCollectionsStillHaveBoundedStructuralWeight()
  {
    using var fixture = new CacheFixture();
    var result = Result(0);
    result.State!.Plan!.Route.Legs = Enumerable.Repeat(new RouteLeg(0, 0, []), 17_000).ToList();
    fixture.Cache.Store("empty-legs", result);

    Assert.Null(fixture.Cache.Get("empty-legs"));
  }

  [Fact]
  public void CumulativeBudgetEvictsOldestEntryAndRetainsTruckSwitchingWithinBudget()
  {
    using var fixture = new CacheFixture();
    var a = Result(120_000);
    var b = Result(120_000);
    var c = Result(120_000);
    fixture.Cache.Store("a", a);
    fixture.Clock.Advance(TimeSpan.FromSeconds(1));
    fixture.Cache.Store("b", b);

    Assert.Same(a, fixture.Cache.Get("a"));
    Assert.Same(b, fixture.Cache.Get("b"));
    Assert.Same(a, fixture.Cache.Get("a"));

    fixture.Clock.Advance(TimeSpan.FromSeconds(1));
    fixture.Cache.Store("c", c);
    Assert.Null(fixture.Cache.Get("a"));
    Assert.Same(b, fixture.Cache.Get("b"));
    Assert.Same(c, fixture.Cache.Get("c"));
    Assert.Equal(0, fixture.Calls);
  }

  [Fact]
  public void ReplacingAtEntryLimitDoesNotEvictAnUnrelatedEntry()
  {
    using var fixture = new CacheFixture();
    for (var i = 0; i < 100; i++)
    {
      fixture.Cache.Store($"route-{i}", Result(0));
      fixture.Clock.Advance(TimeSpan.FromSeconds(1));
    }
    var replacement = Result(0);
    fixture.Cache.Store("route-99", replacement);

    for (var i = 0; i < 100; i++) Assert.NotNull(fixture.Cache.Get($"route-{i}"));
    Assert.Same(replacement, fixture.Cache.Get("route-99"));

    fixture.Cache.Store("route-100", Result(0));
    Assert.Null(fixture.Cache.Get("route-0"));
    Assert.NotNull(fixture.Cache.Get("route-1"));
    Assert.NotNull(fixture.Cache.Get("route-100"));
  }

  [Fact]
  public void SmallerReplacementReleasesItsPreviousGeometryBudget()
  {
    using var fixture = new CacheFixture();
    var b = Result(120_000);
    fixture.Cache.Store("a", Result(120_000));
    fixture.Cache.Store("b", b);
    var smaller = Result(10);
    fixture.Cache.Store("a", smaller);
    var c = Result(120_000);
    fixture.Cache.Store("c", c);

    Assert.Same(smaller, fixture.Cache.Get("a"));
    Assert.Same(b, fixture.Cache.Get("b"));
    Assert.Same(c, fixture.Cache.Get("c"));
  }

  [Fact]
  public void OversizedReplacementInvalidatesOldValueWithoutEvictingAnotherRoute()
  {
    using var fixture = new CacheFixture();
    var b = Result(120_000);
    fixture.Cache.Store("a", Result(120_000));
    fixture.Cache.Store("b", b);
    fixture.Cache.Store("a", Result(300_000));
    var c = Result(120_000);
    fixture.Cache.Store("c", c);

    Assert.Null(fixture.Cache.Get("a"));
    Assert.Same(b, fixture.Cache.Get("b"));
    Assert.Same(c, fixture.Cache.Get("c"));
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public void NoPlanResponseReleasesGeometryBudget(bool nullResult)
  {
    using var fixture = new CacheFixture();
    var b = Result(120_000);
    fixture.Cache.Store("a", Result(120_000));
    fixture.Cache.Store("b", b);
    fixture.Cache.Store("a", nullResult ? null : Result(0) with { State = null });
    var c = Result(120_000);
    fixture.Cache.Store("c", c);

    Assert.Null(fixture.Cache.Get("a"));
    Assert.Same(b, fixture.Cache.Get("b"));
    Assert.Same(c, fixture.Cache.Get("c"));
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public void ExpiryReleasesGeometryBudgetOnReadOrWrite(bool readExpired)
  {
    using var fixture = new CacheFixture();
    fixture.Cache.Store("a", Result(120_000));
    fixture.Clock.Advance(TimeSpan.FromMinutes(1));
    var b = Result(120_000);
    fixture.Cache.Store("b", b);
    fixture.Clock.Advance(TimeSpan.FromMinutes(4));
    if (readExpired) Assert.Null(fixture.Cache.Get("a"));
    var c = Result(120_000);
    fixture.Cache.Store("c", c);

    Assert.Null(fixture.Cache.Get("a"));
    Assert.Same(b, fixture.Cache.Get("b"));
    Assert.Same(c, fixture.Cache.Get("c"));
  }

  [Fact]
  public void ClearReleasesAllGeometryBudget()
  {
    using var fixture = new CacheFixture();
    fixture.Cache.Store("a", Result(120_000));
    fixture.Cache.Store("b", Result(120_000));
    fixture.Cache.Clear();
    var c = Result(120_000);
    var d = Result(120_000);
    fixture.Cache.Store("c", c);
    fixture.Cache.Store("d", d);

    Assert.Null(fixture.Cache.Get("a"));
    Assert.Null(fixture.Cache.Get("b"));
    Assert.Same(c, fixture.Cache.Get("c"));
    Assert.Same(d, fixture.Cache.Get("d"));
  }

  [Fact]
  public async Task MetadataOnlyRefreshRetainsGeometryWithoutAccumulatingReplacementWeight()
  {
    using var fixture = new CacheFixture();
    var a = Result(60_000);
    var previous = a.State!.Plan!;
    previous.Route.Legs = [new(10, 600, previous.Route.Points)];
    fixture.Cache.Store("a", a);
    var b = Result(120_000);
    fixture.Cache.Store("b", b);
    fixture.Response = a with
    {
      State = a.State with
      {
        Plan = new()
        {
          Id = previous.Id, Version = previous.Version, GeometryOmitted = true,
          Route = new() { Miles = 11, Legs = [new(11, 650, [])] }
        }
      }
    };

    for (var i = 0; i < 3; i++)
    {
      var response = await fixture.Cache.RefreshAsync("a", default);
      var plan = response.Response!.State!.Plan!;
      Assert.False(plan.GeometryOmitted);
      Assert.Same(previous.Route.Points, plan.Route.Points);
      Assert.Same(previous.Route.Legs[0].Points, plan.Route.Legs[0].Points);
      Assert.Equal(11, plan.Route.Miles);
      Assert.Same(response.Response, fixture.Cache.Get("a"));
      Assert.Same(b, fixture.Cache.Get("b"));
    }
    Assert.Equal(3, fixture.Calls);
  }

  [Fact]
  public async Task CompletedRefreshIsReleasedBeforeAReentrantAwaiterStartsAnother()
  {
    var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var calls = 0;
    using var client = new HttpClient(new StubHttpMessageHandler(async (_, _) =>
    {
      if (Interlocked.Increment(ref calls) == 1) await release.Task;
      return new(HttpStatusCode.OK)
      {
        Content = JsonContent.Create(new RequestResponseDTO<AutomaticPlanningResult> { Success = true, Response = Result(0) })
      };
    })) { BaseAddress = new("https://local.test/") };
    var cache = new PlanningDisplayCache(new ApiService(client));
    async Task ReadTwiceAsync()
    {
      Assert.True((await cache.RefreshAsync("a", default)).Success);
      Assert.True((await cache.RefreshAsync("a", default)).Success);
    }
    var previous = SynchronizationContext.Current;
    Task reads;
    try
    {
      SynchronizationContext.SetSynchronizationContext(new InlineSynchronizationContext());
      reads = ReadTwiceAsync();
    }
    finally { SynchronizationContext.SetSynchronizationContext(previous); }
    release.SetResult();
    await reads;

    Assert.Equal(2, calls);
  }

  private sealed class InlineSynchronizationContext : SynchronizationContext
  {
    public override void Post(SendOrPostCallback callback, object? state) => callback(state);
  }

  private static AutomaticPlanningResult Result(int points) => new(Guid.NewGuid(), Guid.NewGuid(), 1,
    new(new(), new()
    {
      Id = Guid.NewGuid(), Version = 1,
      Route = new() { Points = Enumerable.Repeat(new RoutePoint(40, -80), points).ToList() }
    }, null, null, null, true), null);

  private sealed class CacheFixture : IDisposable
  {
    private readonly HttpClient client;
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero));
    public PlanningDisplayCache Cache { get; }
    public AutomaticPlanningResult? Response { get; set; }
    public int Calls { get; private set; }

    public CacheFixture()
    {
      client = new(new StubHttpMessageHandler((_, _) =>
      {
        Calls++;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
          Content = JsonContent.Create(new RequestResponseDTO<AutomaticPlanningResult> { Success = true, Response = Response })
        });
      })) { BaseAddress = new("https://local.test/") };
      Cache = new(new ApiService(client), Clock);
    }

    public void Dispose() => client.Dispose();
  }
}
