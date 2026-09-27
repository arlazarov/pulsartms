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
