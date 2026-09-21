using Application.Features.Routing.Interfaces;
using Domain.Models.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Server.Tests.Support;

namespace Server.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Integration")]
public sealed class PlanningRefreshStoreTests
{
  private static readonly TimeSpan Lease = TimeSpan.FromMinutes(4);

  [Fact]
  public async Task RepeatedDemandSurvivesRestartAndCoalescesAcrossContexts()
  {
    await using var f = await PlanningRefreshFixture.CreateAsync();
    var work = new PlanningScope(Guid.NewGuid(), null);
    Assert.True(
      (await f.Store.RequestAsync(work, "one", f.Now, default)).Pending
    );
    await using var other = f.NewScope();
    var store =
      other.ServiceProvider.GetRequiredService<IPlanningRefreshStore>();
    Assert.True(
      (await store.RequestAsync(work, "one", f.Now, default)).Pending
    );
    var row = await f.Db.PlanningRefreshRequests.SingleAsync();
    Assert.Equal(1, row.RequestedVersion);
    var claimed = Assert.IsType<PlanningRefreshWork>(
      await store.ClaimAsync(f.Now, Lease, default)
    );
    Assert.Equal(work, claimed.Scope);
    Assert.Null(await f.Store.ClaimAsync(f.Now, Lease, default));
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task NewInputsDuringCalculationRemainPending(bool succeeded)
  {
    await using var f = await PlanningRefreshFixture.CreateAsync();
    var work = new PlanningScope(Guid.NewGuid(), null);
    await f.Store.RequestAsync(work, "first", f.Now, default);
    var old = Assert.IsType<PlanningRefreshWork>(
      await f.Store.ClaimAsync(f.Now, Lease, default)
    );
    await f.Store.RequestAsync(work, "second", f.Now, default);
    Assert.Null(await f.Store.ClaimAsync(f.Now, Lease, default));
    Assert.True(
      await f.Store.CompleteAsync(
        old,
        succeeded,
        f.Now,
        f.Now.AddMinutes(10),
        default
      )
    );
    var next = Assert.IsType<PlanningRefreshWork>(
      await f.Store.ClaimAsync(f.Now, Lease, default)
    );
    Assert.Equal(old.Id, next.Id);
    Assert.Equal(old.Version + 1, next.Version);
    Assert.Equal(1, next.Attempts);
    Assert.NotEqual(old.LeaseId, next.LeaseId);
  }

  [Fact]
  public async Task ExpiryRecoversWorkAndRejectsOldAcknowledgements()
  {
    await using var f = await PlanningRefreshFixture.CreateAsync();
    await f.Store.RequestAsync(
      new(Guid.NewGuid(), null),
      "one",
      f.Now,
      default
    );
    var old = Assert.IsType<PlanningRefreshWork>(
      await f.Store.ClaimAsync(f.Now, Lease, default)
    );
    f.Time.Advance(Lease);
    Assert.False(await f.Store.CompleteAsync(old, true, f.Now, f.Now, default));
    var current = Assert.IsType<PlanningRefreshWork>(
      await f.Store.ClaimAsync(f.Now, Lease, default)
    );
    Assert.Equal(2, current.Attempts);
    Assert.False(
      await f.Store.CompleteAsync(old, false, f.Now, f.Now, default)
    );
    Assert.True(
      await f.Store.CompleteAsync(current, true, f.Now, f.Now, default)
    );
    Assert.False(
      await f.Store.CompleteAsync(current, false, f.Now, f.Now, default)
    );
    Assert.Null(await f.Store.ClaimAsync(f.Now, Lease, default));
  }

  [Fact]
  public async Task RetryDeadlineSurvivesDemandAndAllowsNewInputImmediately()
  {
    await using var f = await PlanningRefreshFixture.CreateAsync();
    var work = new PlanningScope(Guid.NewGuid(), null);
    await f.Store.RequestAsync(work, "one", f.Now, default);
    var first = Assert.IsType<PlanningRefreshWork>(
      await f.Store.ClaimAsync(f.Now, Lease, default)
    );
    await f.Store.CompleteAsync(
      first,
      false,
      f.Now,
      f.Now.AddMinutes(2),
      default
    );
    await f.Store.RequestAsync(work, "one", f.Now, default);
    Assert.Null(
      await f.Store.ClaimAsync(f.Now.AddSeconds(119), Lease, default)
    );
    var retry = Assert.IsType<PlanningRefreshWork>(
      await f.Store.ClaimAsync(f.Now.AddMinutes(2), Lease, default)
    );
    Assert.Equal(first.Version, retry.Version);
    Assert.Equal(2, retry.Attempts);
    f.Time.Advance(TimeSpan.FromMinutes(2));
    await f.Store.CompleteAsync(
      retry,
      false,
      f.Now,
      f.Now.AddMinutes(2),
      default
    );
    await f.Store.RequestAsync(work, "changed", f.Now, default);
    var changed = Assert.IsType<PlanningRefreshWork>(
      await f.Store.ClaimAsync(f.Now, Lease, default)
    );
    Assert.Equal(first.Version + 1, changed.Version);
    Assert.Equal(1, changed.Attempts);
  }

  [Fact]
  public async Task CompletedCooldownIsDurableAndReopensOnlyOnNewDemand()
  {
    await using var f = await PlanningRefreshFixture.CreateAsync();
    var work = new PlanningScope(Guid.NewGuid(), null);
    await f.Store.RequestAsync(work, "one", f.Now, default);
    var first = Assert.IsType<PlanningRefreshWork>(
      await f.Store.ClaimAsync(f.Now, Lease, default)
    );
    await f.Store.CompleteAsync(
      first,
      true,
      f.Now,
      f.Now.AddMinutes(2),
      default
    );
    Assert.False(
      (await f.Store.RequestAsync(work, "one", f.Now, default)).Pending
    );
    f.Time.Advance(TimeSpan.FromMinutes(2));
    Assert.Null(await f.Store.ClaimAsync(f.Now, Lease, default));
    Assert.True(
      (await f.Store.RequestAsync(work, "one", f.Now, default)).Pending
    );
    var next = Assert.IsType<PlanningRefreshWork>(
      await f.Store.ClaimAsync(f.Now, Lease, default)
    );
    Assert.Equal(first.Version + 1, next.Version);
  }

  [Fact]
  public async Task SameLoadLegsAndAssignmentRevisionsKeepIndependentRequests()
  {
    await using var f = await PlanningRefreshFixture.CreateAsync();
    var load = Guid.NewGuid();
    var first = new PlanningScope(load, Guid.NewGuid(), 1);
    var scopes = new[]
    {
      new PlanningScope(load, null),
      first,
      new PlanningScope(load, Guid.NewGuid(), 1),
      first with
      {
        AssignmentRevision = 2,
      },
    };
    foreach (var scope in scopes)
      await f.Store.RequestAsync(scope, "one", f.Now, default);
    var claimed = new HashSet<PlanningScope>();
    for (var i = 0; i < scopes.Length; i++)
    {
      var work = Assert.IsType<PlanningRefreshWork>(
        await f.Store.ClaimAsync(f.Now, Lease, default)
      );
      Assert.True(claimed.Add(work.Scope));
    }
    Assert.True(claimed.SetEquals(scopes));
    Assert.Null(await f.Store.ClaimAsync(f.Now, Lease, default));
  }

  [Fact]
  public async Task PruningRetainsUnacknowledgedWork()
  {
    await using var f = await PlanningRefreshFixture.CreateAsync();
    var completed = new PlanningScope(Guid.NewGuid(), null);
    await f.Store.RequestAsync(completed, "one", f.Now, default);
    var work = Assert.IsType<PlanningRefreshWork>(
      await f.Store.ClaimAsync(f.Now, Lease, default)
    );
    await f.Store.CompleteAsync(work, true, f.Now, f.Now, default);
    var pending = new PlanningScope(Guid.NewGuid(), null);
    await f.Store.RequestAsync(pending, "one", f.Now, default);
    await f.Store.PruneAsync(f.Now.AddDays(1), default);
    Assert.Equal(
      pending.DispatchId,
      (await f.Db.PlanningRefreshRequests.SingleAsync()).DispatchId
    );
  }
}
