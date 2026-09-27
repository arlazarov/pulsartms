using Application.Features.Eta.Services;
using Domain.Models.Eta;
using Server.Tests.Support;

namespace Server.Tests.Eta;

// ETA memory held scopes it was never asked to view - leg identities,
// published summary answers, forecasts - for the life of the process, and
// only a process running the ETA worker ever swept even the viewed ones.
// Every scope is now touched when written or read, forgotten in every map
// after ten idle minutes, and bounded in number whatever the process runs.
[Trait("Category", "Eta")]
[Trait("Kind", "Unit")]
public sealed class EtaMemoryBoundTests
{
  private static readonly DateTimeOffset Start = new(
    2026,
    9,
    27,
    19,
    0,
    0,
    TimeSpan.Zero
  );

  [Fact]
  public void AnIdleScopeIsForgottenInEveryMap()
  {
    var time = new ManualTimeProvider(Start);
    using var memory = new EtaMemory(time);
    var dispatch = Guid.NewGuid();
    var leg = Guid.NewGuid();
    var kept = Guid.NewGuid();

    memory.SummaryAnswerChange(dispatch, leg, hasEta: true);
    memory.Publish(leg, Entry());
    memory.NoteMapAnswer(leg, "current");
    time.Advance(TimeSpan.FromMinutes(9));
    memory.Publish(kept, Entry());
    time.Advance(TimeSpan.FromMinutes(2));
    _ = memory.Due(time.GetUtcNow().UtcDateTime).ToArray();

    Assert.False(memory.Results.ContainsKey(leg));
    Assert.Null(memory.Resolve(leg).ExecutionLegId);
    Assert.Null(memory.MapAnswer(leg));
    // Forgotten, so the same answer is a change again.
    Assert.Equal("shown", memory.SummaryAnswerChange(dispatch, leg, true));
    Assert.True(memory.Results.ContainsKey(kept));
  }

  // A viewed scope lives as long as it is viewed, as before.
  [Fact]
  public void AViewedScopeIsKeptWhileItIsViewed()
  {
    var time = new ManualTimeProvider(Start);
    using var memory = new EtaMemory(time);
    var scope = Guid.NewGuid();
    memory.Publish(scope, Entry());

    for (var i = 0; i < 3; i++)
    {
      time.Advance(TimeSpan.FromMinutes(6));
      memory.View(scope, time.GetUtcNow().UtcDateTime);
      _ = memory.Due(time.GetUtcNow().UtcDateTime).ToArray();
    }

    Assert.True(memory.Results.ContainsKey(scope));
  }

  // Past the bound the least recently touched go, down to three quarters,
  // so trimming is rare: 1,524 new scopes cost two trims, not 500 sorts.
  [Fact]
  public void TheNumberOfScopesIsBounded()
  {
    var time = new ManualTimeProvider(Start);
    using var memory = new EtaMemory(time);
    var scopes = Enumerable
      .Range(0, EtaMemory.MaximumScopes + 500)
      .Select(_ => Guid.NewGuid())
      .ToArray();

    foreach (var scope in scopes)
    {
      time.Advance(TimeSpan.FromMilliseconds(10));
      memory.Publish(scope, Entry());
    }

    var held = memory.ReadMemory().Single(x => x.Name == "eta-current");
    Assert.InRange(held.Entries ?? 0, 1, EtaMemory.MaximumScopes);
    Assert.True(memory.Results.ContainsKey(scopes[^1]));
    Assert.False(memory.Results.ContainsKey(scopes[0]));
    Assert.Equal(2, memory.Trims);
  }

  // Views alone - the map and the summaries read scopes they never write
  // to - count against the bound as well.
  [Fact]
  public void ViewedScopesAreBoundedToo()
  {
    var time = new ManualTimeProvider(Start);
    using var memory = new EtaMemory(time);

    for (var i = 0; i < EtaMemory.MaximumScopes + 500; i++)
    {
      time.Advance(TimeSpan.FromMilliseconds(10));
      memory.View(Guid.NewGuid(), time.GetUtcNow().UtcDateTime);
    }

    Assert.InRange(memory.Viewed.Count, 1, EtaMemory.MaximumScopes);
  }

  // A forget arrives while a write holds the scope, between its touch and
  // its forecast: it waits, then drops both. A write never leaves a
  // forecast the bound does not count.
  [Fact]
  public async Task AForgetDuringAWriteDropsTheWholeScope()
  {
    using var memory = new EtaMemory(new ManualTimeProvider(Start));
    var scope = Guid.NewGuid();
    Task? forget = null;
    bool? forgetWaited = null;
    memory.AfterTouch = key =>
    {
      if (key != scope || forget is not null)
        return;
      forget = Task.Run(() => memory.Forget(scope));
      // The forget needs the lock this write holds; it cannot finish yet.
      forgetWaited = !forget.Wait(TimeSpan.FromMilliseconds(200));
    };

    memory.Publish(scope, Entry());
    await forget!.WaitAsync(TimeSpan.FromSeconds(5));

    Assert.True(forgetWaited);
    Assert.False(memory.Results.ContainsKey(scope));
    Assert.False(memory.Tracks(scope));
  }

  // Due decided from its snapshot that the scope was idle; before it
  // forgets, the scope is published again. The fresh forecast stays.
  [Fact]
  public void AScopeRefreshedAfterDuesSnapshotIsKept()
  {
    var time = new ManualTimeProvider(Start);
    using var memory = new EtaMemory(time);
    var scope = Guid.NewGuid();
    memory.Publish(scope, Entry());
    time.Advance(TimeSpan.FromMinutes(11));
    var fresh = Entry() with { Signature = "fresh" };
    memory.BeforeForget = key =>
    {
      if (key == scope)
        memory.Publish(scope, fresh);
    };

    _ = memory.Due(time.GetUtcNow().UtcDateTime).ToArray();

    Assert.Same(fresh, memory.Results[scope]);
    Assert.True(memory.Tracks(scope));
  }

  // The bound picked the least recently touched scope from its snapshot;
  // it is viewed before the bound forgets it, and stays.
  [Fact]
  public void AScopeTouchedAfterTheBoundsSnapshotIsKept()
  {
    var time = new ManualTimeProvider(Start);
    using var memory = new EtaMemory(time);
    var oldest = Guid.NewGuid();
    memory.Publish(oldest, Entry());
    memory.BeforeForget = key =>
    {
      if (key == oldest)
        memory.View(oldest, time.GetUtcNow().UtcDateTime);
    };

    for (var i = 0; i < EtaMemory.MaximumScopes; i++)
    {
      time.Advance(TimeSpan.FromMilliseconds(10));
      memory.Publish(Guid.NewGuid(), Entry());
    }

    Assert.Equal(1, memory.Trims);
    Assert.True(memory.Results.ContainsKey(oldest));
    Assert.True(memory.Tracks(oldest));
  }

  // A leg's identity - which load it belongs to - lives and goes with the
  // leg's forecast: kept while the scope is used, forgotten with it.
  [Fact]
  public void ALegsIdentityGoesWithItsForecast()
  {
    var time = new ManualTimeProvider(Start);
    using var memory = new EtaMemory(time);
    var dispatch = Guid.NewGuid();
    var leg = memory.Scope(dispatch, Guid.NewGuid());
    memory.Publish(leg, Entry());
    time.Advance(TimeSpan.FromMinutes(6));
    memory.View(leg, time.GetUtcNow().UtcDateTime);
    time.Advance(TimeSpan.FromMinutes(6));
    _ = memory.Due(time.GetUtcNow().UtcDateTime).ToArray();
    var kept = memory.Resolve(leg);
    time.Advance(TimeSpan.FromMinutes(11));
    _ = memory.Due(time.GetUtcNow().UtcDateTime).ToArray();

    Assert.Equal(dispatch, kept.DispatchId);
    Assert.Equal(leg, kept.ExecutionLegId);
    Assert.False(memory.Results.ContainsKey(leg));
    Assert.Null(memory.Resolve(leg).ExecutionLegId);
    Assert.False(memory.Tracks(leg));
  }

  private static EtaMemory.Entry Entry() =>
    new(
      "signature",
      new DispatchEta(
        Start.UtcDateTime,
        Start.UtcDateTime.AddMinutes(10),
        [],
        null,
        []
      )
    );
}
