using Application.Features.Routing.Models;
using Application.Features.Routing.Services.Routes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Server.Tests.Support;

namespace Server.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Integration")]
public sealed class PlanningRefreshQueueTests
{
  [Fact]
  public async Task RecentRoadSkipsDemandButMissingOrChangedInputsDoNot()
  {
    await using var f = await PlanningRefreshFixture.CreateAsync();
    var plan = new RoutePlan { CalculatedAt = f.Now };
    var state = new RoutePlanningState(new(), plan, null, null, null, true);
    var work = new PlanningScope(Guid.NewGuid(), null);
    Assert.Null(await f.Queue.EnqueueAsync(work, state, "one", default));
    Assert.Empty(await f.Db.PlanningRefreshRequests.ToListAsync());
    plan.InputsChanged = true;
    Assert.True(
      (await f.Queue.EnqueueAsync(work, state, "one", default))?.Pending
    );
    plan.InputsChanged = false;
    Assert.Null(await f.Queue.EnqueueAsync(work, state, "one", default));
    f.Time.Advance(TimeSpan.FromMinutes(3));
    Assert.True(
      (await f.Queue.EnqueueAsync(work, state, "one", default))?.Pending
    );
    Assert.True(
      (
        await f.Queue.EnqueueAsync(
          new(Guid.NewGuid(), null),
          state with
          {
            Plan = null,
          },
          "one",
          default
        )
      )?.Pending
    );
  }

  [Fact]
  public async Task StatusPreservesProviderErrorsAndNeverInventsRunningWork()
  {
    await using var f = await PlanningRefreshFixture.CreateAsync();
    var id = Guid.NewGuid();
    var state = new RoutePlanningState(new(), null, null, null, null, true);
    Assert.Equal("Route update pending.", f.Queue.Message(id, state, "one"));
    var requested = await f.Queue.EnqueueAsync(
      new(id, null),
      state,
      "one",
      default
    );
    Assert.Equal(
      "Route update queued.",
      f.Queue.Message(id, state, "one", pending: requested?.Pending == true)
    );
    f.Services.GetRequiredService<IMemoryCache>()
      .Set(
        PlanningRefreshQueue.ErrorKey(id, state, "one"),
        "Address lookup failed."
      );
    Assert.Equal("Address lookup failed.", f.Queue.Message(id, state, "one"));
  }

  [Theory]
  [InlineData("work")]
  [InlineData("profile")]
  [InlineData("choice")]
  public async Task NewInputBypassesLocalDemandMemo(string change)
  {
    await using var f = await PlanningRefreshFixture.CreateAsync();
    var state = new RoutePlanningState(new(), null, null, null, null, true);
    var scope = new PlanningScope(Guid.NewGuid(), null);
    await f.Queue.EnqueueAsync(scope, state, "one", default);
    var identity = "one";
    if (change == "work")
      identity = "two";
    else if (change == "choice")
      state = state with { RouteChoiceRevision = 1 };
    else
      state.Profile.HeightFeet += 1;
    await f.Queue.EnqueueAsync(scope, state, identity, default);
    Assert.Equal(
      2,
      (await f.Db.PlanningRefreshRequests.SingleAsync()).RequestedVersion
    );
  }

  [Fact]
  public async Task FailedPersistenceDoesNotMemoizeUncommittedDemand()
  {
    await using var f = await PlanningRefreshFixture.CreateAsync();
    var state = new RoutePlanningState(new(), null, null, null, null, true);
    var scope = new PlanningScope(Guid.NewGuid(), null);
    await using (var transaction = await f.Db.Database.BeginTransactionAsync())
      await Assert.ThrowsAsync<InvalidOperationException>(
        () => f.Queue.EnqueueAsync(scope, state, "one", default)
      );
    Assert.True(
      (await f.Queue.EnqueueAsync(scope, state, "one", default))?.Pending
    );
    Assert.Single(await f.Db.PlanningRefreshRequests.ToListAsync());
  }
}
