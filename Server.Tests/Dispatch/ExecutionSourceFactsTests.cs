using Application.Features.Dispatch.Models;
using Application.Features.Execution.Models;
using Domain.Entities.Dispatch;
using Domain.Entities.Execution;
using DispatchEntity = Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Unit")]
public sealed class ExecutionSourceFactsTests
{
  private static readonly DateTime Now = new(
    2026,
    9,
    14,
    12,
    0,
    0,
    DateTimeKind.Utc
  );

  [Fact]
  public void VerifiedAddressRetainsItsLocationWhenOriginalSourceReplays()
  {
    var supplied = Stop();
    supplied.SourceAddressJson = StopAddress.From(supplied).Serialize();
    var retained = ExecutionSnapshots.Copy(supplied);
    retained.Address = "123 Main Street";
    retained.Latitude = 35.1m;
    retained.AddressVerifiedAt = Now.AddMinutes(-5);
    supplied.ArrivedAt = Now.AddMinutes(-1);
    var result = Reconcile(retained, supplied);
    Assert.Null(result.ReviewReason);
    Assert.True(result.Changed);
    var stop = Assert.Single(result.Stops);
    Assert.Equal(retained.Address, stop.Address);
    Assert.Equal(retained.Latitude, stop.Latitude);
    Assert.Equal(supplied.ArrivedAt, stop.ArrivedAt);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public void VerifiedAddressDoesNotAcceptDifferentSourceLocation(
    bool replaceSource
  )
  {
    var retained = Stop();
    retained.SourceAddressJson = StopAddress.From(retained).Serialize();
    retained.AddressVerifiedAt = Now.AddMinutes(-5);
    var supplied = ExecutionSnapshots.Copy(retained);
    supplied.AddressVerifiedAt = null;
    supplied.Address = "Different terminal";
    if (replaceSource)
      supplied.SourceAddressJson = StopAddress.From(supplied).Serialize();
    var result = Reconcile(retained, supplied);
    Assert.NotNull(result.ReviewReason);
    Assert.False(result.Changed);
    Assert.Equal(retained.Address, Assert.Single(result.Stops).Address);
  }

  [Fact]
  public void ExactOrdinaryDeliveryFillsFactAndCanCompleteActiveLeg()
  {
    var stop = Stop();
    var supplied = ExecutionSnapshots.Copy(stop);
    supplied.DeliveredAt = Now.AddMinutes(-10);
    var result = Reconcile(stop, supplied);
    Assert.True(result.Changed);
    Assert.Null(result.ReviewReason);
    Assert.Equal(supplied.DeliveredAt, result.CompletedAt);
    Assert.Null(stop.DeliveredAt);
  }

  [Fact]
  public void RescheduledDeliveryUpdatesWindowAndArrivalWithoutChangingIdentity()
  {
    var stop = Stop();
    stop.ScheduledDate = new(2026, 9, 14);
    stop.ScheduledTime = new(5, 0);
    var supplied = ExecutionSnapshots.Copy(stop);
    supplied.ScheduledDate = new(2026, 9, 16);
    supplied.ScheduledTime = new(9, 0);
    supplied.ScheduledDate2 = new(2026, 9, 17);
    supplied.ScheduledTime2 = new(11, 0);
    supplied.IsWindow = true;
    supplied.ArrivedAt = Now.AddMinutes(-10);
    var result = Reconcile(stop, supplied);
    var updated = Assert.Single(result.Stops);
    Assert.True(result.Changed);
    Assert.Null(result.ReviewReason);
    Assert.Null(result.CompletedAt);
    Assert.Equal(StopAppointment.From(supplied), StopAppointment.From(updated));
    Assert.Equal(supplied.ArrivedAt, updated.ArrivedAt);
    Assert.Equal(stop.Id, updated.Id);
    Assert.Equal(stop.Address, updated.Address);
    Assert.Equal(new DateOnly(2026, 9, 14), stop.ScheduledDate);
    Assert.False(Reconcile(updated, supplied).Changed);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public void AppointmentDoesNotBypassNativeTransferOrAddressProtection(
    bool native
  )
  {
    var stop = Stop();
    var supplied = ExecutionSnapshots.Copy(stop);
    supplied.ScheduledDate = new(2026, 9, 16);
    if (!native)
      supplied.Address = "Different terminal";
    var result = Reconcile(stop, supplied, native: native);
    Assert.False(result.Changed);
    Assert.Null(Assert.Single(result.Stops).ScheduledDate);
  }

  [Theory]
  [InlineData(-1)]
  [InlineData(1)]
  public void KnownActualCannotMoveBackwardOrForward(int hours)
  {
    var stop = Stop();
    stop.DeliveredAt = Now.AddHours(-2);
    var supplied = ExecutionSnapshots.Copy(stop);
    supplied.DeliveredAt = stop.DeliveredAt.Value.AddHours(hours);
    var result = Reconcile(stop, supplied);
    Assert.False(result.Changed);
    Assert.NotNull(result.ReviewReason);
    Assert.Null(result.CompletedAt);
    Assert.Equal(stop.DeliveredAt, Assert.Single(result.Stops).DeliveredAt);
  }

  [Fact]
  public void SourceCannotCompleteNativeHookOrRelease()
  {
    var stop = Stop();
    stop.Job = "Hook";
    var supplied = ExecutionSnapshots.Copy(stop);
    supplied.DepartedAt = Now.AddMinutes(-5);
    var result = Reconcile(stop, supplied, native: true);
    Assert.False(result.Changed);
    Assert.Null(result.CompletedAt);
    Assert.Null(Assert.Single(result.Stops).DepartedAt);
  }

  [Fact]
  public void PlannedTransferCannotReceiveActualSourceEventsBeforeReceipt()
  {
    var stop = Stop();
    var supplied = ExecutionSnapshots.Copy(stop);
    supplied.DeliveredAt = Now.AddMinutes(-5);
    var result = Reconcile(stop, supplied, planned: true);
    Assert.NotNull(result.ReviewReason);
    Assert.Null(Assert.Single(result.Stops).DeliveredAt);
  }

  [Fact]
  public void RecordingTimeCannotRejectAnEarlierActualWhenStartIsUnknown()
  {
    var stop = Stop();
    var supplied = ExecutionSnapshots.Copy(stop);
    supplied.DeliveredAt = Now.AddHours(-2);
    var leg = Leg();
    leg.StartedAt = null;
    leg.RecordedAt = Now.AddHours(-1);

    var result = ExecutionSourceFacts.Reconcile(
      leg,
      [stop],
      new() { Stops = [supplied] },
      new HashSet<Guid>(),
      false,
      Now
    );

    Assert.Null(result.ReviewReason);
    Assert.Equal(supplied.DeliveredAt, result.CompletedAt);
    Assert.Null(leg.StartedAt);
    Assert.Null(stop.DeliveredAt);
  }

  [Fact]
  public void FutureActualAndDeliveryBeforeActivationAreRejected()
  {
    foreach (var at in new[] { Now.AddDays(1), Now.AddDays(-2) })
    {
      var stop = Stop();
      var supplied = ExecutionSnapshots.Copy(stop);
      supplied.DeliveredAt = at;
      var result = Reconcile(stop, supplied);
      Assert.NotNull(result.ReviewReason);
      Assert.Null(result.CompletedAt);
      Assert.Null(Assert.Single(result.Stops).DeliveredAt);
    }
  }

  [Fact]
  public void SourceGeometryCannotSilentlyMoveNativeVisit()
  {
    var stop = Stop();
    var supplied = ExecutionSnapshots.Copy(stop);
    supplied.Address = "A different terminal";
    var result = Reconcile(stop, supplied);
    Assert.NotNull(result.ReviewReason);
    Assert.Equal(stop.Address, Assert.Single(result.Stops).Address);
  }

  [Fact]
  public void MergedEventsMustRemainInChronologicalOrder()
  {
    var stop = Stop();
    stop.ArrivedAt = Now.AddHours(-1);
    var supplied = ExecutionSnapshots.Copy(stop);
    supplied.ArrivedAt = null;
    supplied.DeliveredAt = Now.AddHours(-2);
    var result = Reconcile(stop, supplied);
    Assert.NotNull(result.ReviewReason);
    Assert.False(result.Changed);
    Assert.Null(Assert.Single(result.Stops).DeliveredAt);
  }

  [Fact]
  public void FinalDeliveryBeforeIndependentReleaseCannotCloseOutgoingLeg()
  {
    var stop = Stop();
    var supplied = ExecutionSnapshots.Copy(stop);
    supplied.DeliveredAt = Now.AddMinutes(-5);
    var leg = Leg();
    leg.EndSwitchId = Guid.NewGuid();
    var result = ExecutionSourceFacts.Reconcile(
      leg,
      [stop],
      new() { Stops = [supplied] },
      new HashSet<Guid>(),
      false,
      Now
    );
    Assert.True(result.Changed);
    Assert.Null(result.CompletedAt);
  }

  [Fact]
  public void EveryPartialChronologyPreservesEventOrderWithoutChangingInput()
  {
    DateTime?[] times =
    [
      null,
      Now.AddHours(-3),
      Now.AddHours(-2),
      Now.AddHours(-1),
    ];
    foreach (var arrived in times)
    foreach (var pickedUp in times)
    foreach (var delivered in times)
    foreach (var departed in times)
    {
      var retained = Stop();
      var supplied = ExecutionSnapshots.Copy(retained);
      supplied.ArrivedAt = arrived;
      supplied.PickedUpAt = pickedUp;
      supplied.DeliveredAt = delivered;
      supplied.DepartedAt = departed;
      var events = new[] { arrived, pickedUp, delivered, departed }
        .Where(x => x.HasValue)
        .Select(x => x!.Value)
        .ToArray();
      var ordered = events.SequenceEqual(events.Order());
      var result = Reconcile(retained, supplied);
      var stop = Assert.Single(result.Stops);
      Assert.Equal(ordered, result.ReviewReason is null);
      Assert.Equal(ordered && events.Length > 0, result.Changed);
      Assert.Equal(ordered ? arrived : null, stop.ArrivedAt);
      Assert.Equal(ordered ? pickedUp : null, stop.PickedUpAt);
      Assert.Equal(ordered ? delivered : null, stop.DeliveredAt);
      Assert.Equal(ordered ? departed : null, stop.DepartedAt);
      Assert.Null(retained.ArrivedAt);
      Assert.Null(retained.PickedUpAt);
      Assert.Null(retained.DeliveredAt);
      Assert.Null(retained.DepartedAt);
    }
  }

  private static ExecutionSourceUpdate Reconcile(
    DispatchStop stop,
    DispatchStop supplied,
    bool native = false,
    bool planned = false
  )
  {
    var leg = Leg();
    leg.Status = planned ? "planned" : "active";
    leg.StartSwitchId = planned ? Guid.NewGuid() : null;
    return ExecutionSourceFacts.Reconcile(
      leg,
      [stop],
      new DispatchEntity { Stops = [supplied] },
      native ? new HashSet<Guid> { stop.Id } : new HashSet<Guid>(),
      false,
      Now
    );
  }

  private static ExecutionLeg Leg() =>
    new()
    {
      Id = Guid.NewGuid(),
      Status = "active",
      StartedAt = Now.AddDays(-1),
      RecordedAt = Now.AddDays(-1),
    };

  private static DispatchStop Stop() =>
    new()
    {
      Id = Guid.NewGuid(),
      Job = "Drop Off",
      Address = "123 Main St",
      Latitude = 35,
      Longitude = -80,
    };
}
