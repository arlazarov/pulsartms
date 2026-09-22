using Application.Caching;
using Application.Features.Routing.Background;
using Application.Features.Synchronization.Options;
using Domain.Entities.Dispatch;
using Domain.Policies;
using Microsoft.Extensions.Options;
using DispatchEntity = global::Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Unit")]
public sealed class RoutePreparationQueueTests : IDisposable
{
  private readonly ReadCache reads = new(
    Options.Create(new SynchronizationOptions())
  );

  public void Dispose() => reads.Dispose();

  [Theory]
  [InlineData("address")]
  [InlineData("truck")]
  [InlineData("dispatch")]
  public void CompletedAssignedDemandSurvivesLaterInputNotifications(
    string notification
  )
  {
    var queue = Create(Clock());
    var truck = Guid.NewGuid();
    var id = Guid.NewGuid();
    queue.Request(id, truck);
    queue.Complete(Assert.Single(queue.Take(1)), "ready", truck);
    if (notification == "address")
      queue.AddressesChanged([id], [truck]);
    else if (notification == "truck")
      queue.MarkTruckDirty(truck, reads);
    else
      queue.MarkDirty(id);
    var work = Assert.Single(queue.Take(1));
    Assert.True(work.Explicit);
    queue.Complete(work, "updated", truck);
    queue.Request(id, truck);
    Assert.Empty(queue.Take(1));
  }

  [Fact]
  public void AssignedDemandRetainsBackoffAndRepairsWithoutRepeatedWork()
  {
    var clock = Clock();
    var queue = Create(clock);
    var truck = Guid.NewGuid();
    var id = Guid.NewGuid();
    queue.Request(id, truck);
    var first = Assert.Single(queue.Take(10));
    Assert.True(first.Explicit);
    queue.Retry(first, "input", truck);
    for (var attempt = 0; attempt < 10; attempt++)
      queue.Request(id, truck);
    Assert.Empty(queue.Take(10));
    clock.Advance(TimeSpan.FromMinutes(10));
    var retry = Assert.Single(queue.Take(10));
    queue.Complete(retry, "input", truck);
    queue.Request(id, truck);
    Assert.Empty(queue.Take(10));
    clock.Advance(TimeSpan.FromHours(6));
    queue.Request(id, truck);
    var repair = Assert.Single(queue.Take(10));
    Assert.True(repair.Explicit);
    queue.Complete(repair, "input", truck);
    queue.MarkTruckDirty(truck, reads);
    queue.Request(id, truck);
    Assert.Single(queue.Take(10));
  }

  [Fact]
  public void AssignedDemandUsesQueueCapacityAndRetriesDroppedWork()
  {
    var queue = Create(Clock(), pending: 1);
    var truck = Guid.NewGuid();
    var first = Guid.NewGuid();
    var next = Guid.NewGuid();
    queue.Request(first, truck);
    queue.Request(next, truck);
    Assert.Equal(1, queue.PendingCount);
    var work = Assert.Single(queue.Take(10));
    Assert.Equal(first, work.DispatchId);
    queue.Complete(work, "ready", truck);
    queue.Request(first, truck);
    queue.Request(next, truck);
    Assert.Equal(next, Assert.Single(queue.Take(10)).DispatchId);
  }

  [Fact]
  public void UnchangedSuccessWaitsForRepairWhileChangedInputRunsImmediately()
  {
    var clock = Clock();
    var queue = Create(clock);
    var id = Guid.NewGuid();
    queue.Observe(id, null, "one", 1);
    var first = Assert.Single(queue.Take(10));
    queue.Complete(first, "one", null);
    clock.Advance(TimeSpan.FromMinutes(1));
    queue.Observe(id, null, "one", 1);
    Assert.Empty(queue.Take(10));
    queue.Observe(id, null, "two", 1);
    var changed = Assert.Single(queue.Take(10));
    queue.Complete(changed, "two", null);
    clock.Advance(TimeSpan.FromHours(6));
    queue.Observe(id, null, "two", 1);
    Assert.Single(queue.Take(10));
  }

  [Fact]
  public void RepeatedDemandPreservesRetryBudgetAndChangedDemandBypassesIt()
  {
    var clock = Clock();
    var queue = Create(clock);
    var id = Guid.NewGuid();
    queue.Request(id, "one");
    var work = Assert.Single(queue.Take(10));
    queue.Retry(
      work,
      "input",
      null,
      clock.GetUtcNow().AddMinutes(10).UtcDateTime
    );
    for (var index = 0; index < 10; index++)
    {
      queue.Request(id, "one");
      queue.Observe(id, null, "input", 1);
    }
    Assert.Empty(queue.Take(10));
    clock.Advance(TimeSpan.FromMinutes(10));
    var retry = Assert.Single(queue.Take(10));
    Assert.True(retry.Explicit);
    queue.Retry(retry, "input", null);
    queue.Request(id, "two");
    Assert.Single(queue.Take(10));
  }

  [Fact]
  public void ChangedWorkDuringAnAttemptCannotBeLostByItsCompletion()
  {
    var queue = Create(Clock());
    var id = Guid.NewGuid();
    queue.Request(id, "one");
    var old = Assert.Single(queue.Take(10));
    queue.MarkDirty(id);
    queue.Complete(old, "old", null);
    var current = Assert.Single(queue.Take(10));
    Assert.True(current.Version > old.Version);
    Assert.Empty(queue.Take(10));
  }

  [Fact]
  public void ChangedTruckInvalidatesKnownSuccessorsButNotUnrelatedTrucks()
  {
    var queue = Create(Clock());
    var truck = Guid.NewGuid();
    var affected = Guid.NewGuid();
    var unrelated = Guid.NewGuid();
    queue.Observe(affected, truck, "one", 1);
    queue.Observe(unrelated, Guid.NewGuid(), "two", 1);
    foreach (var work in queue.Take(10))
      queue.Complete(
        work,
        work.Fingerprint!,
        queue.Identity(work.DispatchId).TruckId
      );
    queue.MarkTruckDirty(truck, reads);
    var pending = Assert.Single(queue.Take(10));
    Assert.Equal(affected, pending.DispatchId);
    Assert.Equal(1, pending.ConnectionVersion);
    Assert.Equal(0, queue.Identity(unrelated).ConnectionVersion);
  }

  [Fact]
  public void CapacityKeepsOlderEqualPriorityAndDroppedDemandCanBeRequestedAgain()
  {
    var clock = Clock();
    var queue = Create(clock, pending: 2);
    var first = Guid.NewGuid();
    var second = Guid.NewGuid();
    var dropped = Guid.NewGuid();
    queue.Request(first, "first");
    clock.Advance(TimeSpan.FromSeconds(1));
    queue.Request(second, "second");
    queue.Request(dropped, "dropped");
    Assert.Equal(2, queue.PendingCount);
    var work = Assert.Single(queue.Take(1));
    Assert.Equal(first, work.DispatchId);
    queue.Complete(work, "ready", null);
    queue.Request(dropped, "dropped");
    Assert.Equal(
      new[] { second, dropped },
      queue.Take(10).Select(x => x.DispatchId)
    );
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public void DroppedRetryOfAPreviouslyCompletedLoadStillHonorsItsDeadline(
    bool changedDemand
  )
  {
    var clock = Clock();
    var queue = Create(clock, pending: 1);
    var id = Guid.NewGuid();
    queue.Request(id, "old", 2);
    queue.Complete(Assert.Single(queue.Take(1)), "old", null);
    var identity = changedDemand ? "new" : "old";
    if (changedDemand)
      queue.Request(id, identity, 2);
    else
    {
      clock.Advance(TimeSpan.FromHours(6));
      queue.Observe(id, null, "old", 2);
    }
    queue.Retry(
      Assert.Single(queue.Take(1)),
      identity,
      null,
      clock.GetUtcNow().AddMinutes(10).UtcDateTime
    );
    var urgent = Guid.NewGuid();
    queue.Request(urgent, "urgent", 0);
    queue.Complete(Assert.Single(queue.Take(1)), "ready", null);
    queue.Request(id, identity, 2);
    Assert.Empty(queue.Take(10));
    clock.Advance(TimeSpan.FromMinutes(10));
    Assert.Equal(id, Assert.Single(queue.Take(10)).DispatchId);
  }

  [Fact]
  public void StateCapacityRemainsHardEvenWhenEveryEntryIsRunning()
  {
    var queue = Create(Clock(), pending: 2, state: 2);
    queue.Request(Guid.NewGuid(), "one");
    queue.Request(Guid.NewGuid(), "two");
    var running = queue.Take(2);
    var later = Guid.NewGuid();
    queue.Request(later, "later");
    Assert.Equal(2, queue.StateCount);
    Assert.Equal(0, queue.PendingCount);
    queue.Complete(running[0], "ready", null);
    queue.Request(later, "later");
    Assert.Equal(2, queue.StateCount);
    Assert.Equal(later, Assert.Single(queue.Take(1)).DispatchId);
  }

  [Fact]
  public void ClearingAnAddressRetryWakesPreparationWithoutRepeatedPollingWork()
  {
    var clock = Clock();
    using var reads = TestCache.Create();
    var queue = Create(clock);
    var truck = Guid.NewGuid();
    var retry = clock.GetUtcNow().AddDays(1).UtcDateTime;
    var load = new DispatchEntity
    {
      Id = Guid.NewGuid(),
      TruckId = truck,
      Status = "assigned",
      Stops =
      [
        new DispatchStop
        {
          Id = Guid.NewGuid(),
          Sequence = 1,
          Address = "1 Arizona Way",
          AddressRetryAfter = retry,
        },
      ],
    };
    string Signature() =>
      RoutePreparationInputs.Signature(
        load,
        truck,
        0,
        reads,
        clock.GetUtcNow().UtcDateTime
      );
    queue.Observe(load.Id, truck, Signature(), 1);
    queue.Retry(Assert.Single(queue.Take(1)), Signature(), truck, retry);
    for (var i = 0; i < 3; i++)
    {
      clock.Advance(TimeSpan.FromMinutes(1));
      queue.Observe(load.Id, truck, Signature(), 1);
      Assert.Empty(queue.Take(1));
    }

    load.Stops[0].AddressRetryAfter = null;
    queue.Observe(load.Id, truck, Signature(), 1);
    var work = Assert.Single(queue.Take(1));
    queue.Complete(work, Signature(), truck);
    queue.Observe(load.Id, truck, Signature(), 1);
    Assert.Empty(queue.Take(1));
  }

  [Fact]
  public void FinancialAndProfileChangesDirtyInputsButUnrelatedDispatchInvalidationDoesNot()
  {
    using var reads = TestCache.Create();
    var truck = Guid.NewGuid();
    var load = new DispatchEntity
    {
      Id = Guid.NewGuid(),
      TruckId = truck,
      Status = "assigned",
      Price = 100,
      Currency = "USD",
      LoadedMiles = 50,
      Stops =
      [
        new DispatchStop
        {
          Id = Guid.NewGuid(),
          Sequence = 1,
          Latitude = 40,
          Longitude = -80,
        },
      ],
    };
    string Signature() =>
      RoutePreparationInputs.Signature(
        load,
        truck,
        0,
        reads,
        Clock().GetUtcNow().UtcDateTime
      );
    var original = Signature();
    reads.Invalidate("dispatch");
    Assert.Equal(original, Signature());
    load.Price = 101;
    Assert.NotEqual(original, Signature());
    var financial = Signature();
    reads.Invalidate($"profile:{truck}");
    Assert.NotEqual(financial, Signature());
  }

  private static ManualTimeProvider Clock() =>
    new(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero));

  private static RoutePreparationQueue Create(
    ManualTimeProvider clock,
    int pending = 512,
    int state = 2048
  ) =>
    new(
      Options.Create(
        new RoutePreparationOptions
        {
          PendingCapacity = pending,
          StateCapacity = state,
        }
      ),
      clock
    );
}
