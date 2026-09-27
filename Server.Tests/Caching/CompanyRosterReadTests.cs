using Application.Caching;
using Application.Features.Synchronization.Options;
using Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Server.Tests.Support;

namespace Server.Tests.Caching;

// Clock-driven work asks for the carriers every time it wakes, some loops
// every second. The list is read once per lifetime and shared; each
// carrier is still served its own turn on every pass.
[Trait("Category", "Caching")]
[Trait("Kind", "Unit")]
public sealed class CompanyRosterReadTests
{
  private static readonly Guid A = Guid.NewGuid();
  private static readonly Guid B = Guid.NewGuid();
  private static readonly Guid C = Guid.NewGuid();

  [Fact]
  public async Task RepeatedPassesReadTheRosterOnceAndServeEveryCarrier()
  {
    var roster = new CountingRoster([A, B]);
    await using var provider = Services(roster, TestCache.Create());
    var served = new List<Guid?>();

    for (var pass = 0; pass < 20; pass++)
      await Pass(provider, served);

    Assert.Equal(1, roster.Calls);
    Assert.Equal(20, served.Count(x => x == A));
    Assert.Equal(20, served.Count(x => x == B));
  }

  [Fact]
  public async Task AFreshRosterServesANewCarrierAndLeavesARetiredOne()
  {
    var roster = new CountingRoster([A, B]);
    var reads = TestCache.Create();
    await using var provider = Services(roster, reads);
    var served = new List<Guid?>();
    await Pass(provider, served);

    roster.Active = [B, C];
    served.Clear();
    await Pass(provider, served);
    Assert.Equal([A, B], served);

    reads.InvalidateGlobally(ReadGroups.Companies);
    served.Clear();
    await Pass(provider, served);
    Assert.Equal([B, C], served);
    Assert.Equal(2, roster.Calls);
  }

  [Fact]
  public async Task OverlappingPassesShareOneRead()
  {
    var roster = new CountingRoster([A, B])
    {
      Delay = TimeSpan.FromMilliseconds(50),
    };
    await using var provider = Services(roster, TestCache.Create());

    await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Pass(provider, [])));

    Assert.Equal(1, roster.Calls);
  }

  // Nobody tells the roster read that a carrier joined or left: the
  // lifetime alone brings the change in, on the host's clock.
  [Fact]
  public async Task ACarrierJoiningOrLeavingIsServedOnceTheLifetimeRunsOut()
  {
    var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
    var roster = new CountingRoster([A, B]);
    await using var provider = Services(roster, Reads(time));
    var served = new List<Guid?>();
    await Pass(provider, served);

    roster.Active = [B, C];
    time.Advance(TimeSpan.FromSeconds(29));
    served.Clear();
    await Pass(provider, served);
    Assert.Equal([A, B], served);
    Assert.Equal(1, roster.Calls);

    time.Advance(TimeSpan.FromSeconds(2));
    served.Clear();
    await Pass(provider, served);
    Assert.Equal([B, C], served);
    Assert.Equal(2, roster.Calls);
  }

  // Every loop wakes at once after the roster expired: they still read it
  // once between them.
  [Fact]
  public async Task OverlappingPassesAfterExpiryShareOneRead()
  {
    var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
    var roster = new CountingRoster([A, B])
    {
      Delay = TimeSpan.FromMilliseconds(50),
    };
    await using var provider = Services(roster, Reads(time));
    await Pass(provider, []);

    time.Advance(CompanyPasses.RosterLifetime + TimeSpan.FromSeconds(1));
    await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Pass(provider, [])));

    Assert.Equal(2, roster.Calls);
  }

  [Fact]
  public async Task APassWithCarriersButNoReadCacheRefusesToRun()
  {
    var services = new ServiceCollection();
    services.AddSingleton<ICurrentCompany>(new ServedCompany());
    services.AddSingleton<ICompanyRoster>(new CountingRoster([A]));
    await using var provider = services.BuildServiceProvider();

    await Assert.ThrowsAsync<InvalidOperationException>(
      () => Pass(provider, [])
    );
  }

  private static ReadCache Reads(TimeProvider time) =>
    new(Options.Create(new SynchronizationOptions()), null, time);

  private static async Task Pass(ServiceProvider provider, List<Guid?> served)
  {
    var current = provider.GetRequiredService<ICurrentCompany>();
    await using var scope = provider.CreateAsyncScope();
    await CompanyPasses.ForEachCompanyAsync(
      scope.ServiceProvider,
      _ =>
      {
        lock (served)
          served.Add(current.Id);
        return Task.CompletedTask;
      },
      default
    );
  }

  private static ServiceProvider Services(
    ICompanyRoster roster,
    ReadCache reads
  )
  {
    var services = new ServiceCollection();
    services.AddSingleton<ICurrentCompany>(new ServedCompany());
    services.AddSingleton(roster);
    services.AddSingleton<IReadCache>(reads);
    return services.BuildServiceProvider();
  }

  private sealed class CountingRoster(Guid[] active) : ICompanyRoster
  {
    private int calls;
    public Guid[] Active { get; set; } = active;
    public TimeSpan Delay { get; init; }
    public int Calls => Volatile.Read(ref calls);

    public async Task<IReadOnlyList<Guid>> ActiveAsync(CancellationToken ct)
    {
      Interlocked.Increment(ref calls);
      if (Delay > TimeSpan.Zero)
        await Task.Delay(Delay, ct);
      return Active;
    }
  }

  // Nobody is served until a pass chooses a carrier.
  private sealed class ServedCompany : ICurrentCompany
  {
    private readonly AsyncLocal<Guid?> chosen = new();
    public Guid? Id => chosen.Value;

    public IDisposable As(Guid company)
    {
      var previous = chosen.Value;
      chosen.Value = company;
      return new Restore(() => chosen.Value = previous);
    }

    private sealed class Restore(Action restore) : IDisposable
    {
      public void Dispose() => restore();
    }
  }
}
