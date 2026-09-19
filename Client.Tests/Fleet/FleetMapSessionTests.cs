using Client.Pages.FleetMap;
using Client.Tests.Support;
using Microsoft.JSInterop;

namespace Client.Tests.Fleet;

[Trait("Category", "Fleet")]
[Trait("Kind", "Unit")]
public sealed class FleetMapSessionTests
{
  [Fact]
  public async Task ConcurrentStartsShareInitializationAndDisposalReleasesEveryOwnedReferenceOnce()
  {
    var fixture = new Fixture();
    var options = new TaskCompletionSource<object?>(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    fixture.Map.Respond = (_, _) => options.Task;
    var start = fixture.Session.StartAsync(
      default,
      "key",
      new { trafficVisible = true }
    );
    var duplicate = fixture.Session.StartAsync(
      default,
      "key",
      new { trafficVisible = false }
    );
    Assert.Same(start, duplicate);
    Assert.Single(fixture.Module.Calls);
    options.SetResult(null);
    await start;
    Assert.Same(fixture.Map, fixture.Session.Map);
    await fixture.Session.DisposeAsync();
    await fixture.Session.DisposeAsync();
    Assert.Null(fixture.Session.Map);
    Assert.Equal(1, fixture.Map.DisposeCount);
    Assert.Equal(1, fixture.Module.DisposeCount);
    Assert.Single(fixture.Map.Calls, x => x.Name == "dispose");
    Assert.Throws<ObjectDisposedException>(() => fixture.Callback!.Value);
    await fixture.Session.StartAsync(default, "key", new { });
    Assert.Single(fixture.Module.Calls);
  }

  [Fact]
  public async Task FailedDynamicImportRetriesOnceWithANewUrl()
  {
    var fixture = new Fixture();
    fixture.Runtime.Respond = (_, _) =>
      fixture.Runtime.Calls.Count == 1
        ? Task.FromException<object?>(
          new JSException(
            "Failed to fetch dynamically imported module: resource"
          )
        )
        : Task.FromResult<object?>(fixture.Module);
    await using var session = fixture.Session;
    await session.StartAsync(default, "key", new { });
    var urls = fixture
      .Runtime.Calls.Select(x => Assert.IsType<string>(x.Args![0]))
      .ToArray();
    Assert.Equal(2, urls.Length);
    Assert.Equal("./js/generated/fleetMap/fleetMap.js", urls[0]);
    Assert.StartsWith(urls[0] + "?retry=", urls[1]);
  }

  [Fact]
  public async Task OtherImportFailuresAreNotAutomaticallyRetriedButAllowExplicitRetry()
  {
    var fixture = new Fixture();
    fixture.Runtime.Respond = (_, _) =>
      Task.FromException<object?>(new JSException("Access denied"));
    await using var session = fixture.Session;
    await Assert.ThrowsAsync<JSException>(
      () => session.StartAsync(default, "key", new { })
    );
    Assert.Single(fixture.Runtime.Calls);
    fixture.Runtime.Respond = (_, _) =>
      Task.FromResult<object?>(fixture.Module);
    await session.StartAsync(default, "key", new { });
    Assert.Same(fixture.Map, session.Map);
  }

  [Fact]
  public async Task FailedOptionsReleaseThePartialMapAndAllowRetryWithoutReimporting()
  {
    var fixture = new Fixture();
    fixture.Map.Respond = (method, _) =>
      method == "setOptions"
        ? Task.FromException<object?>(new JSException("Options failed"))
        : Task.FromResult<object?>(null);
    await using var session = fixture.Session;
    await Assert.ThrowsAsync<JSException>(
      () => session.StartAsync(default, "key", new { })
    );
    Assert.Null(session.Map);
    Assert.Equal(1, fixture.Map.DisposeCount);
    var replacement = new MapInteropStub();
    fixture.Module.Respond = (_, _) => Task.FromResult<object?>(replacement);
    await session.StartAsync(default, "key", new { });
    Assert.Same(replacement, session.Map);
    Assert.Single(fixture.Runtime.Calls);
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task DisposalDuringStartupWaitsForResourcesAndDoesNotInitializeALateMap(
    bool duringImport
  )
  {
    var fixture = new Fixture();
    var pending = new TaskCompletionSource<object?>(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    if (duringImport)
      fixture.Runtime.Respond = (_, _) => pending.Task;
    else
      fixture.Module.Respond = (_, _) => pending.Task;
    var start = fixture.Session.StartAsync(default, "key", new { });
    var dispose = fixture.Session.DisposeAsync().AsTask();
    Assert.False(dispose.IsCompleted);
    pending.SetResult(duringImport ? fixture.Module : fixture.Map);
    await Task.WhenAll(start, dispose);
    Assert.Null(fixture.Session.Map);
    Assert.DoesNotContain(fixture.Map.Calls, x => x.Name == "setOptions");
    Assert.Equal(1, fixture.Module.DisposeCount);
    Assert.Equal(duringImport ? 0 : 1, fixture.Map.DisposeCount);
  }

  private sealed class Fixture
  {
    public MapInteropStub Runtime { get; } = new();
    public MapInteropStub Module { get; } = new();
    public MapInteropStub Map { get; } = new();
    public DotNetObjectReference<object>? Callback { get; private set; }
    public FleetMapSession<object> Session { get; }

    public Fixture()
    {
      Runtime.Respond = (_, _) => Task.FromResult<object?>(Module);
      Module.Respond = (_, args) =>
      {
        Callback = Assert.IsType<DotNetObjectReference<object>>(args![2]);
        return Task.FromResult<object?>(Map);
      };
      Session = new(Runtime, new object());
    }
  }
}
