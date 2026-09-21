using Application.Features.Routing.Services.Deadheads;

namespace Server.Tests.Routing;

using Load = global::Domain.Entities.Dispatch.Dispatch;

// The load import writes what the provider sent, word for word: the stop's
// job and the load's status are never normalised. So every question asked
// of them has to accept what providers actually say - and the answers
// differ from one file to the next.
//
// These are written from what the code already knows is possible: the
// fixtures across this suite carry "Pickup" and "pickup" beside "Pick Up",
// and ExecutionWorkRelevance excludes a load cancelled under either
// spelling because one spelled the other way was once driven across the
// map.
[Trait("Category", "Finance")]
[Trait("Kind", "Unit")]
public sealed class DeadheadVocabularyTests
{
  [Theory]
  [InlineData("Pick Up")]
  [InlineData("PICK UP")]
  [InlineData("Pickup")]
  [InlineData("pickup")]
  public void AnyPickupSpellingStillConnectsToThePrecedingLoad(string job)
  {
    var truck = Guid.NewGuid();
    var previous = Delivered(truck, 11);
    var next = Assigned(truck, 14, job);

    Assert.NotNull(DeadheadConnection.Find(next, [previous]));
  }

  [Theory]
  [InlineData("cancelled")]
  [InlineData("canceled")]
  public void ALoadCancelledAtTheSourceNeverOwnsTheConnection(string status)
  {
    var truck = Guid.NewGuid();
    var cancelled = Delivered(truck, 11);
    cancelled.Status = status;
    var next = Assigned(truck, 14, "Pick Up");

    Assert.Null(DeadheadConnection.Find(next, [cancelled]));
  }

  private static Load Delivered(Guid truck, int day)
  {
    var load = Assigned(truck, day, "Pick Up");
    load.Status = "completed";
    load.ExecutionStatus = "completed";
    load.ExecutionLegId = Guid.NewGuid();
    load.Stops[^1].DeliveredAt = new DateTime(2026, 9, day, 18, 0, 0);
    return load;
  }

  private static Load Assigned(Guid truck, int day, string pickupJob) =>
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
          Job = pickupJob,
          ScheduledDate = new(2026, 9, day),
          ScheduledTime = new(8, 0),
        },
        new()
        {
          Id = Guid.NewGuid(),
          Sequence = 2,
          Job = "Drop Off",
          ScheduledDate = new(2026, 9, day + 1),
          ScheduledTime = new(14, 0),
        },
      ],
    };
}
