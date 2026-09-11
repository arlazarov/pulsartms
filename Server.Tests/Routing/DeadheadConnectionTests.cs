using Application.Features.Routing.Services.Deadheads;
using Application.Features.Routing.Models;
using Domain.Entities.Dispatch;

namespace Server.Tests.Routing;

using Load = global::Domain.Entities.Dispatch.Dispatch;

[Trait("Category", "Finance")]
[Trait("Kind", "Unit")]
public sealed class DeadheadConnectionTests
{
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
    Assert.NotEqual(hash, pair.Signature(profile));
    hash = pair.Signature(profile);
    profile.HeightFeet = 14;
    Assert.NotEqual(hash, pair.Signature(profile));
  }

  [Theory]
  [InlineData(10, 14)]
  [InlineData(11, 14)]
  public void AppointmentOverlapKeepsTheUnambiguousPredecessor(int deliveryDay, int deliveryHour)
  {
    var truck = Guid.NewGuid();
    var previous = Make(truck, 4, 19, deliveryDay, deliveryHour);
    var next = Make(truck, 10, 10, 15, 8);

    var connection = Assert.IsType<DeadheadConnection>(DeadheadConnection.Find(next, [previous]));

    Assert.Equal(previous.Id, connection.Previous.Id);
    Assert.Same(previous.Stops[^1], connection.From);
    Assert.Same(next.Stops[0], connection.To);
  }

  [Fact]
  public void UnknownDeliveryTimeDoesNotMakeKnownPickupOrderAmbiguous()
  {
    var truck = Guid.NewGuid();
    var previous = Make(truck, 4, 19, 10, 14);
    var next = Make(truck, 10, 10, 15, 8);
    previous.Stops[^1].ScheduledTime = null;

    Assert.Equal(previous.Id, DeadheadConnection.Find(next, [previous])!.Previous.Id);
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

  private static Load Make(Guid truck, int pickupDay, int pickupHour, int deliveryDay, int deliveryHour) => new()
  {
    Id = Guid.NewGuid(), TruckId = truck, Status = "assigned", Stops =
    [
      new() { Id = Guid.NewGuid(), Sequence = 1, Job = "Pick Up", ScheduledDate = new(2026, 9, pickupDay),
        ScheduledTime = new(pickupHour, 0) },
      new() { Id = Guid.NewGuid(), Sequence = 2, Job = "Drop Off", ScheduledDate = new(2026, 9, deliveryDay),
        ScheduledTime = new(deliveryHour, 0) }
    ]
  };
}
