using Domain.Models.Routing;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Server.Tests.Support;

namespace Server.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Integration")]
public sealed class SourceRoadStoreTests
{
  private static readonly TimeSpan Lease = TimeSpan.FromMinutes(3);

  [Fact]
  public async Task ExplicitDemandSurvivesContextReplacementAndCoalesces()
  {
    await using var f = await SourceRoadFixture.CreateAsync();
    var id = Guid.NewGuid();
    await f.Store.DemandAsync(id, "geometry", 0, f.Now, default);
    await using var second = f.Connect();
    var store = new SourceRoadStore(second);
    await store.DemandAsync(id, "geometry", 0, f.Now, default);
    var work = Assert.IsType<SourceRoadWork>(
      await store.ClaimAsync(f.Now, Lease, default)
    );
    Assert.Equal(id, work.DispatchId);
    Assert.Equal(1, work.Version);
    Assert.True(work.Explicit);
    Assert.Null(await f.Store.ClaimAsync(f.Now, Lease, default));
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task ChangedInputsCannotBeAcknowledgedByOlderWork(bool succeeded)
  {
    await using var f = await SourceRoadFixture.CreateAsync();
    var id = Guid.NewGuid();
    var truck = Guid.NewGuid();
    await f.Store.ObserveAsync(
      id,
      truck,
      "one",
      2,
      false,
      false,
      f.Now,
      default
    );
    var first = Assert.IsType<SourceRoadWork>(
      await f.Store.ClaimAsync(f.Now, Lease, default)
    );
    await f.Store.ObserveAsync(
      id,
      null,
      "two",
      2,
      false,
      false,
      f.Now,
      default
    );
    Assert.Null(await f.Store.ClaimAsync(f.Now, Lease, default));
    Assert.True(
      await f.Store.CompleteAsync(
        first,
        succeeded,
        f.Now,
        f.Now.AddHours(1),
        default
      )
    );
    var next = Assert.IsType<SourceRoadWork>(
      await f.Store.ClaimAsync(f.Now, Lease, default)
    );
    Assert.Equal(first.Version + 1, next.Version);
    Assert.Equal(1, next.Attempts);
    Assert.Null(next.TruckId);
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task ObservationAndDemandKeepOneRetryDeadline(bool demandFirst)
  {
    await using var f = await SourceRoadFixture.CreateAsync();
    var id = Guid.NewGuid();
    if (demandFirst)
      await f.Store.DemandAsync(id, "geometry", 2, f.Now, default);
    else
      await f.Store.ObserveAsync(
        id,
        null,
        "inputs",
        2,
        false,
        false,
        f.Now,
        default
      );
    var first = Assert.IsType<SourceRoadWork>(
      await f.Store.ClaimAsync(f.Now, Lease, default)
    );
    var deadline = f.Now.AddMinutes(2);
    await f.Store.CompleteAsync(first, false, f.Now, deadline, default);
    for (var index = 0; index < 3; index++)
    {
      await using var other = f.Connect();
      var store = new SourceRoadStore(other);
      await store.DemandAsync(id, "geometry", 0, f.Now, default);
      await store.ObserveAsync(
        id,
        null,
        "inputs",
        1,
        false,
        true,
        f.Now,
        default
      );
      Assert.Null(
        await store.ClaimAsync(deadline.AddSeconds(-1), Lease, default)
      );
    }
    var retry = Assert.IsType<SourceRoadWork>(
      await f.Store.ClaimAsync(deadline, Lease, default)
    );
    Assert.Equal(first.Version, retry.Version);
    Assert.Equal(2, retry.Attempts);
    Assert.True(retry.Explicit);
  }

  [Fact]
  public async Task ExpiredOwnerCannotCompleteRecoveredWork()
  {
    await using var f = await SourceRoadFixture.CreateAsync();
    await f.Store.DemandAsync(Guid.NewGuid(), "one", 0, f.Now, default);
    var old = Assert.IsType<SourceRoadWork>(
      await f.Store.ClaimAsync(f.Now, Lease, default)
    );
    f.Time.Advance(Lease);
    Assert.False(await f.Store.CompleteAsync(old, true, f.Now, f.Now, default));
    await using var replacement = f.Connect();
    var store = new SourceRoadStore(replacement);
    var current = Assert.IsType<SourceRoadWork>(
      await store.ClaimAsync(f.Now, Lease, default)
    );
    Assert.Equal(2, current.Attempts);
    Assert.NotEqual(old.LeaseId, current.LeaseId);
    Assert.False(
      await f.Store.CompleteAsync(old, false, f.Now, f.Now, default)
    );
    Assert.True(
      await store.CompleteAsync(current, true, f.Now, f.Now, default)
    );
    Assert.False(
      await store.CompleteAsync(current, true, f.Now, f.Now, default)
    );
  }

  [Fact]
  public async Task CompletedCooldownRequiresChangedInputOrAnExplicitRepairHint()
  {
    await using var f = await SourceRoadFixture.CreateAsync();
    var id = Guid.NewGuid();
    await f.Store.ObserveAsync(
      id,
      null,
      "one",
      0,
      false,
      false,
      f.Now,
      default
    );
    var first = Assert.IsType<SourceRoadWork>(
      await f.Store.ClaimAsync(f.Now, Lease, default)
    );
    await f.Store.CompleteAsync(first, true, f.Now, f.Now.AddHours(1), default);
    await f.Store.ObserveAsync(
      id,
      null,
      "one",
      0,
      false,
      false,
      f.Now,
      default
    );
    Assert.Null(await f.Store.ClaimAsync(f.Now, Lease, default));
    await f.Store.ObserveAsync(id, null, "one", 0, false, true, f.Now, default);
    var repair = Assert.IsType<SourceRoadWork>(
      await f.Store.ClaimAsync(f.Now, Lease, default)
    );
    Assert.Equal(first.Version + 1, repair.Version);
  }

  [Fact]
  public async Task CleanupRetainsPendingWorkAndRecentCompletion()
  {
    await using var f = await SourceRoadFixture.CreateAsync();
    var old = f.Now.AddDays(-10);
    var completed = Guid.NewGuid();
    await f.Store.DemandAsync(completed, "one", 0, old, default);
    var work = Assert.IsType<SourceRoadWork>(
      await f.Store.ClaimAsync(old, Lease, default)
    );
    await f.Store.CompleteAsync(work, true, old, old.AddHours(1), default);
    await f.Store.DemandAsync(Guid.NewGuid(), "pending", 0, old, default);
    await f.Store.DemandAsync(Guid.NewGuid(), "recent", 0, f.Now, default);
    await f.Store.PruneAsync(f.Now.AddDays(-7), default);
    var retained = await f.Db.SourceRoadRequests.ToListAsync();
    Assert.Equal(2, retained.Count);
    Assert.DoesNotContain(retained, x => x.DispatchId == completed);
  }
}
