using Domain.Entities.Dispatch;
using Domain.Models.Execution;

namespace Server.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Unit")]
public sealed class ExecutionSourceChangesTests
{
  [Fact]
  public void PreviewChangesOnlyFutureLocationAndAppointment()
  {
    var original = Stop();
    var source = ExecutionSnapshots.Copy(original);
    source.Address = "456 New St";
    source.ScheduledDate = new(2026, 9, 20);
    var result = Preview(original, source);
    Assert.Empty(result.Problems);
    Assert.Equal(original.Id, Assert.Single(result.ChangedVisitIds));
    var updated = Assert.Single(result.Stops);
    Assert.Equal(source.Address, updated.Address);
    Assert.Equal(source.ScheduledDate, updated.ScheduledDate);
    Assert.Equal("Loaded", updated.StateAfter);
    Assert.Equal("123 Main St", original.Address);
    Assert.Null(updated.DeliveredAt);
  }

  [Fact]
  public void SourceCannotMoveAlreadyArrivedVisit()
  {
    var original = Stop();
    original.ArrivedAt = DateTime.UtcNow.AddHours(-1);
    var source = ExecutionSnapshots.Copy(original);
    source.Address = "456 New St";
    var result = Preview(original, source);
    Assert.NotEmpty(result.Problems);
    Assert.Empty(result.ChangedVisitIds);
    Assert.Equal(original.Address, Assert.Single(result.Stops).Address);
  }

  [Fact]
  public void SourceCannotRewriteNativeTransferOrCargo()
  {
    var original = Stop();
    var source = ExecutionSnapshots.Copy(original);
    source.Address = "456 New St";
    var native = ExecutionSourceChanges.Preview(
      [original],
      [source],
      new HashSet<Guid> { original.Id }
    );
    Assert.Empty(native.ChangedVisitIds);
    Assert.Equal(original.Address, Assert.Single(native.Stops).Address);
    source.ManualStateAfter = "Empty";
    var cargo = Preview(original, source);
    Assert.NotEmpty(cargo.Problems);
    Assert.Equal("Loaded", Assert.Single(cargo.Stops).StateAfter);
  }

  private static ExecutionSourceChanges Preview(
    DispatchStop original,
    DispatchStop source
  ) =>
    ExecutionSourceChanges.Preview([original], [source], new HashSet<Guid>());

  private static DispatchStop Stop() =>
    new()
    {
      Id = Guid.NewGuid(),
      Job = "Waypoint",
      StateAfter = "Loaded",
      Address = "123 Main St",
      Latitude = 35,
      Longitude = -80,
    };
}
