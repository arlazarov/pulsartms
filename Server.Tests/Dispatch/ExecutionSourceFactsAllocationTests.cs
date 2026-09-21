using Domain.Entities.Dispatch;
using Domain.Entities.Execution;
using Domain.Models.Execution;
using Xunit.Abstractions;
using DispatchEntity = Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Allocation")]
public sealed class ExecutionSourceFactsAllocationTests(
  ITestOutputHelper output
)
{
  [Fact]
  public void FullSnapshotReconciliationKeepsTemporaryAllocationBounded()
  {
    var now = new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);
    var leg = new ExecutionLeg
    {
      Status = "active",
      StartedAt = now.AddDays(-1),
    };
    var source = new DispatchEntity
    {
      Stops = Enumerable
        .Range(1, 49)
        .Select(i => new DispatchStop
        {
          Id = Guid.NewGuid(),
          Sequence = i,
          Job = "Waypoint",
          ArrivedAt = now.AddHours(-2),
          DepartedAt = now.AddHours(-1),
        })
        .ToList(),
    };
    var snapshot = source.Stops.Select(ExecutionSnapshots.Copy).ToList();
    var native = new HashSet<Guid>();
    for (var i = 0; i < 20; i++)
      Reconcile();
    var before = GC.GetAllocatedBytesForCurrentThread();
    const int count = 100;
    for (var i = 0; i < count; i++)
      Reconcile();
    var allocated = (GC.GetAllocatedBytesForCurrentThread() - before) / count;
    output.WriteLine($"49-stop reconciliation allocated {allocated:N0} bytes.");
    Assert.InRange(allocated, 1, 49 * 1_100);
    Assert.Equal(49, snapshot.Count);

    void Reconcile() =>
      ExecutionSourceFacts.Reconcile(leg, snapshot, source, native, false, now);
  }
}
