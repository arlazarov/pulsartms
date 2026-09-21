using Application.Features.Dispatch.Models;
using Application.Features.Routing.Services.Deadheads;
using Domain.Entities.Dispatch;
using Domain.Models.Routing;

namespace Server.Tests.Routing;

using Load = global::Domain.Entities.Dispatch.Dispatch;

[Trait("Category", "Finance")]
[Trait("Kind", "Unit")]
public sealed class DeadheadConnectionTests
{
  [Theory]
  [InlineData("Drop Off", false, "sent")]
  [InlineData("Drop Off", false, "completed")]
  [InlineData("Drop Off", false, "assigned")]
  [InlineData("Drop", false, "sent")]
  [InlineData("Drop Off", true, "sent")]
  public void ActiveNativeDeliveryOwnsTheNextConnectionBeforeCompletedHistory(
    string finalOperation,
    bool awaitingReceipt,
    string oldStatus
  )
  {
    var truck = Guid.NewGuid();
    var old = Make(truck, 12, 12, 13, 13);
    old.Status = oldStatus;
    old.Stops[^1].DeliveredAt = new DateTime(2026, 9, 13, 18, 27, 47);
    var released = Make(truck, 14, 11, 14, 7);
    released.Status = "completed";
    released.ExecutionStatus = "completed";
    released.ExecutionLegId = Guid.NewGuid();
    released.Stops[^1].Job = "Drop";
    var active = Make(truck, 11, 8, 14, 5);
    active.Status = "in_transit";
    active.ExecutionStatus = "active";
    active.ExecutionLegId = Guid.NewGuid();
    active.ShipDate = new(2026, 9, 11);
    active.Stops[0].Job = "Hook";
    active.Stops[0].ScheduledDate = null;
    active.Stops[0].ScheduledTime = null;
    active.Stops[0].AwaitingHandoff = awaitingReceipt;
    active.Stops[^1].Job = finalOperation;
    var first = Make(truck, 16, 14, 18, 4);
    var second = Make(truck, 18, 11, 20, 11);
    var connection = DeadheadConnection.Find(first, [old, released, active]);
    if (finalOperation == "Drop" || awaitingReceipt)
      Assert.Null(connection);
    else
    {
      Assert.NotNull(connection);
      Assert.Equal(active.Id, connection.Previous.Id);
      Assert.Equal(active.Stops[^1].Id, connection.From.Id);
      Assert.Equal(
        first.Id,
        DeadheadConnection
          .Find(second, [old, released, active, first])!
          .Previous.Id
      );
    }
  }

  [Fact]
  public void InsertionCancellationAndReassignmentChangeThePredecessor()
  {
    var truck = Guid.NewGuid();
    var a = Make(truck, 1, 8, 2, 8);
    var b = Make(truck, 5, 8, 6, 8);
    var c = Make(truck, 3, 8, 4, 8);
    Assert.Equal(a.Id, DeadheadConnection.Find(b, [a, b])!.Previous.Id);
    Assert.Equal(c.Id, DeadheadConnection.Find(b, [a, c, b])!.Previous.Id);
    c.Status = "cancelled";
    Assert.Equal(a.Id, DeadheadConnection.Find(b, [a, c, b])!.Previous.Id);
    c.Status = "assigned";
    c.TruckId = Guid.NewGuid();
    Assert.Equal(a.Id, DeadheadConnection.Find(b, [a, c, b])!.Previous.Id);
  }

  [Fact]
  public void AmbiguousHistoryDoesNotInventEmptyMiles()
  {
    var truck = Guid.NewGuid();
    var a = Make(truck, 1, 8, 2, 8);
    var b = Make(truck, 5, 8, 6, 8);
    var c = Make(truck, 1, 8, 2, 8);
    Assert.Null(DeadheadConnection.Find(b, [a, b, c]));
    c.Stops[0].ScheduledDate = null;
    Assert.Null(DeadheadConnection.Find(b, [a, b, c]));
    a.Stops[1].ScheduledDate = null;
    Assert.Null(DeadheadConnection.Find(b, [a, b]));
    Assert.Null(DeadheadConnection.Find(b, [b]));
  }

  [Fact]
  public void SignatureTracksGeometryAndProfileButNotPrice()
  {
    var truck = Guid.NewGuid();
    var a = Make(truck, 1, 8, 2, 8);
    var b = Make(truck, 5, 8, 6, 8);
    var pair = DeadheadConnection.Find(b, [a, b])!;
    var profile = new TruckRouteProfile();
    var hash = pair.Signature(profile);
    b.Price = 4000;
    Assert.Equal(hash, pair.Signature(profile));
    b.Stops[0].Address = "123 Main St";
    Assert.Equal(hash, pair.Signature(profile));
    pair = DeadheadConnection.Find(b, [a, b])!;
    Assert.NotEqual(hash, pair.Signature(profile));
    hash = pair.Signature(profile);
    profile.HeightFeet = 14;
    Assert.NotEqual(hash, pair.Signature(profile));
  }

  [Fact]
  public void SignatureTracksEndpointReadinessWithoutTimestampChurn()
  {
    var truck = Guid.NewGuid();
    var previous = Make(truck, 1, 8, 2, 8);
    var next = Make(truck, 5, 8, 6, 8);
    var pair = DeadheadConnection.Find(next, [previous])!;
    var stop = previous.Stops[^1];
    stop.Address = "123 Main St";
    stop.City = "Mooresville";
    stop.Latitude = 35.5m;
    stop.Longitude = -80.8m;
    stop.SourceAddressJson = StopAddress.From(stop).Serialize();
    var profile = new TruckRouteProfile();
    string Signature() =>
      DeadheadConnection.Find(next, [previous])!.Signature(profile);
    var unready = Signature();
    stop.AddressRetryAfter = DateTime.UtcNow.AddDays(1);
    var ready = Signature();
    Assert.NotEqual(unready, ready);
    stop.AddressRetryAfter = DateTime.UtcNow.AddDays(2);
    Assert.Equal(ready, Signature());
    stop.AddressRetryAfter = null;
    stop.AddressVerifiedAt = DateTime.UtcNow.AddHours(-1);
    Assert.Equal(ready, Signature());
    stop.AddressVerifiedAt = DateTime.UtcNow.AddDays(-30);
    Assert.Equal(unready, Signature());
  }

  [Theory]
  [InlineData(10, 14)]
  [InlineData(11, 14)]
  public void AppointmentOverlapKeepsTheUnambiguousPredecessor(
    int deliveryDay,
    int deliveryHour
  )
  {
    var truck = Guid.NewGuid();
    var previous = Make(truck, 4, 19, deliveryDay, deliveryHour);
    var next = Make(truck, 10, 10, 15, 8);

    var connection = Assert.IsType<DeadheadConnection>(
      DeadheadConnection.Find(next, [previous])
    );

    Assert.Equal(previous.Id, connection.Previous.Id);
    Assert.Equal(previous.Stops[^1].Id, connection.From.Id);
    Assert.Equal(next.Stops[0].Id, connection.To.Id);
  }

  [Fact]
  public void UnknownDeliveryTimeDoesNotMakeKnownPickupOrderAmbiguous()
  {
    var truck = Guid.NewGuid();
    var previous = Make(truck, 4, 19, 10, 14);
    var next = Make(truck, 10, 10, 15, 8);
    previous.Stops[^1].ScheduledTime = null;

    Assert.Equal(
      previous.Id,
      DeadheadConnection.Find(next, [previous])!.Previous.Id
    );
  }

  [Fact]
  public void TiedPickupOrderAndMixedTruckStopsRemainUnavailable()
  {
    var truck = Guid.NewGuid();
    var previous = Make(truck, 4, 19, 10, 14);
    var next = Make(truck, 10, 10, 15, 8);
    var tied = Make(truck, 4, 19, 10, 13);
    Assert.Null(DeadheadConnection.Find(next, [previous, tied]));
    previous.Stops[^1].TruckId = Guid.NewGuid();
    Assert.Null(DeadheadConnection.Find(next, [previous]));
  }

  private static Load Make(
    Guid truck,
    int pickupDay,
    int pickupHour,
    int deliveryDay,
    int deliveryHour
  ) =>
    new()
    {
      Id = Guid.NewGuid(),
      TruckId = truck,
      Status = "assigned",
      Stops =
      [
        new()
        {
          Id = Guid.NewGuid(),
          Sequence = 1,
          Job = "Pick Up",
          ScheduledDate = new(2026, 9, pickupDay),
          ScheduledTime = new(pickupHour, 0),
        },
        new()
        {
          Id = Guid.NewGuid(),
          Sequence = 2,
          Job = "Drop Off",
          ScheduledDate = new(2026, 9, deliveryDay),
          ScheduledTime = new(deliveryHour, 0),
        },
      ],
    };
}
