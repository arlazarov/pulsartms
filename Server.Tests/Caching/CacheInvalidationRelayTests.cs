using Application.Caching;
using Application.Features.Synchronization.Options;
using Application.Interfaces;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Server.Tests.Support;

namespace Server.Tests.Caching;

// Read caches are per process, so an instance that invalidated a group told
// nobody and a second instance kept answering with reads the first one had
// already dropped. That is why the deployment is pinned to one instance.
// These cover the exchange that lifts the pin.
[Trait("Category", "Caching")]
[Trait("Kind", "Integration")]
public sealed class CacheInvalidationRelayTests
{
  [Fact]
  public async Task AnInstanceKeepsAnsweringUntilTheInvalidationReachesIt()
  {
    await using var f = await Fixture.CreateAsync();
    await Load(f.A, "first");
    await Load(f.B, "first");

    f.A.Cache.Invalidate("board");

    // Nothing has carried it across yet: this is the state the pin protects
    // against, and it is what the rest of these tests measure against.
    Assert.Equal("first", await Load(f.B, "second"));
  }

  [Fact]
  public async Task AnInvalidationOnOneInstanceReachesTheOther()
  {
    await using var f = await Fixture.CreateAsync();
    await Load(f.A, "first");
    await Load(f.B, "first");

    f.A.Cache.Invalidate("board");
    await f.A.Relay.RunOnceAsync(default);
    await f.B.Relay.RunOnceAsync(default);

    Assert.Equal("second", await Load(f.B, "second"));
  }

  [Fact]
  public async Task AnInstanceDoesNotApplyWhatItPublishedItself()
  {
    await using var f = await Fixture.CreateAsync();

    f.A.Cache.Invalidate("board");
    await f.A.Relay.RunOnceAsync(default);
    var generation = f.A.Cache.Generation("board");
    await f.A.Relay.RunOnceAsync(default);

    Assert.Equal(generation, f.A.Cache.Generation("board"));
  }

  [Fact]
  public async Task ARowAlreadyAppliedIsNotAppliedAgain()
  {
    await using var f = await Fixture.CreateAsync();

    f.A.Cache.Invalidate("board");
    await f.A.Relay.RunOnceAsync(default);
    await f.B.Relay.RunOnceAsync(default);
    var generation = f.B.Cache.Generation("board");

    await f.B.Relay.RunOnceAsync(default);
    await f.B.Relay.RunOnceAsync(default);

    Assert.Equal(generation, f.B.Cache.Generation("board"));
  }

  // The reading instance skipped a round, so the row is no longer new when it
  // next looks. It still has to arrive: the cached answer it holds is older
  // than the invalidation.
  [Fact]
  public async Task AnInvalidationArrivesEvenIfTheReaderSkippedARound()
  {
    await using var f = await Fixture.CreateAsync();
    await Load(f.B, "first");

    f.A.Cache.Invalidate("board");
    await f.A.Relay.RunOnceAsync(default);
    f.Clock.Advance(TimeSpan.FromSeconds(30));
    await f.B.Relay.RunOnceAsync(default);

    Assert.Equal("second", await Load(f.B, "second"));
  }

  [Fact]
  public async Task EachInvalidationOfAGroupIsPublishedOnceItIsWritten()
  {
    await using var f = await Fixture.CreateAsync();

    f.A.Cache.Invalidate("board");
    await f.A.Relay.RunOnceAsync(default);
    f.A.Cache.Invalidate("board");
    await f.A.Relay.RunOnceAsync(default);

    Assert.Equal(2, await f.Db.CacheInvalidations.CountAsync());
  }

  // Cached entries expire in minutes, so a row older than an hour can no
  // longer apply to anything still held and only costs storage.
  [Fact]
  public async Task RowsOlderThanAnyCachedEntryArePruned()
  {
    await using var f = await Fixture.CreateAsync();
    f.A.Cache.Invalidate("board");
    await f.A.Relay.RunOnceAsync(default);

    f.Clock.Advance(TimeSpan.FromHours(2));
    await f.A.Relay.RunOnceAsync(default);

    Assert.Equal(0, await f.Db.CacheInvalidations.CountAsync());
  }

  [Fact]
  public async Task OnlyTheInvalidatedGroupIsDropped()
  {
    await using var f = await Fixture.CreateAsync();
    await Load(f.B, "first");
    await f.B.Cache.GetAsync("dispatch", "k", () => Task.FromResult("kept"));

    f.A.Cache.Invalidate("board");
    await f.A.Relay.RunOnceAsync(default);
    await f.B.Relay.RunOnceAsync(default);

    Assert.Equal(
      "kept",
      await f.B.Cache.GetAsync("dispatch", "k", () => Task.FromResult("other"))
    );
  }

  private static Task<string> Load(Instance instance, string value) =>
    instance.Cache.GetAsync("board", "k", () => Task.FromResult(value));

  private sealed record Instance(ReadCache Cache, CacheInvalidationRelay Relay);

  private sealed class Fixture : IAsyncDisposable
  {
    private SqliteConnection Connection { get; init; } = null!;
    private ServiceProvider Services { get; init; } = null!;
    public required AppDbContext Db { get; init; }
    public required ManualTimeProvider Clock { get; init; }
    public required Instance A { get; init; }
    public required Instance B { get; init; }

    public static async Task<Fixture> CreateAsync()
    {
      var connection = new SqliteConnection("Data Source=:memory:");
      await connection.OpenAsync();
      var services = new ServiceCollection()
        .AddDbContext<AppDbContext>(x => x.UseSqlite(connection))
        .AddScoped<IAppDbContext>(x => x.GetRequiredService<AppDbContext>())
        .BuildServiceProvider();
      var db = new AppDbContext(
        new DbContextOptionsBuilder<AppDbContext>()
          .UseSqlite(connection)
          .Options
      );
      await db.Database.EnsureCreatedAsync();
      var clock = new ManualTimeProvider();
      return new Fixture
      {
        Connection = connection,
        Services = services,
        Db = db,
        Clock = clock,
        A = Build(services, clock),
        B = Build(services, clock),
      };
    }

    private static Instance Build(IServiceProvider services, TimeProvider clock)
    {
      var options = Options.Create(new SynchronizationOptions());
      var cache = new ReadCache(options);
      return new(
        cache,
        new CacheInvalidationRelay(
          cache,
          services.GetRequiredService<IServiceScopeFactory>(),
          options,
          clock,
          NullLogger<CacheInvalidationRelay>.Instance
        )
      );
    }

    public async ValueTask DisposeAsync()
    {
      A.Cache.Dispose();
      B.Cache.Dispose();
      await Db.DisposeAsync();
      await Services.DisposeAsync();
      await Connection.DisposeAsync();
    }
  }
}
