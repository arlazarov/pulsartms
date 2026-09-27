using Domain.Entities.Dispatch;
using Domain.Rules;
using Domain.Rules.Routing;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Dispatch;

// Stage 4b: the truck's work is the truck's until the truck has finished
// it, not until the cargo is delivered. A load delivered with a trailer
// still to drop stays on the truck's itinerary; once the drop is done it
// leaves. Before 4b it left at the delivery.
[Trait("Category", "Dispatch")]
[Trait("Kind", "Unit")]
public sealed class TruckWorkMembershipTests
{
  private static readonly DateTime At = new(
    2026,
    9,
    20,
    12,
    0,
    0,
    DateTimeKind.Utc
  );

  [Theory]
  [InlineData(false, true)]
  [InlineData(true, false)]
  public void ATrailerStillToDropKeepsTheLoadTheTrucksWork(
    bool dropped,
    bool stillWork
  )
  {
    var truck = Guid.NewGuid();
    var id = Guid.NewGuid();
    var load = new Load
    {
      Id = id,
      LoadNumber = 1395,
      Status = "in_transit",
      TruckId = truck,
      Stops =
      [
        Stop(id, truck, 1, "Pick Up", pickedUp: At),
        Stop(id, truck, 2, "Drop Off", delivered: At),
        new()
        {
          Id = Guid.NewGuid(),
          DispatchId = id,
          Sequence = 3,
          Job = "Stop",
          TruckId = truck,
          ManualAction = "Drop trailer",
          ManualStateAfter = "Bobtail",
          DepartedAt = dropped ? At : null,
        },
      ],
    };
    var work = RouteWorkProjection.Capture(load.TruckItinerary());

    Assert.Equal(
      stillWork,
      ExecutionWorkRelevance.IsCurrentOrUpcoming(
        work,
        DateOnly.FromDateTime(At),
        includeOverdue: true
      )
    );
    Assert.True(
      CargoDelivery.IsDelivered(work.Stops.Select(CompletionStop.From))
    );
  }

  private static DispatchStop Stop(
    Guid load,
    Guid truck,
    int sequence,
    string job,
    DateTime? pickedUp = null,
    DateTime? delivered = null
  ) =>
    new()
    {
      Id = Guid.NewGuid(),
      DispatchId = load,
      Sequence = sequence,
      Job = job,
      TruckId = truck,
      PickedUpAt = pickedUp,
      DeliveredAt = delivered,
    };
}
