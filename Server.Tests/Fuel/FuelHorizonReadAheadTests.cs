using Application.Features.Routing.Services.FuelPlanning;
using Domain.Entities.Dispatch;
using Domain.Models.Routing;
using Domain.Rules.Routing;
using Server.Tests.Support;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Fuel;

// Reading the horizon's connections and base routes together instead of one
// per loop iteration must fetch exactly what the loop would have fetched -
// no row for a load the loop skips, nothing past the point the loop gives up,
// and no row belonging to a different load.
[Trait("Category", "Fuel")]
[Trait("Kind", "Unit")]
public sealed class FuelHorizonReadAheadTests
{
  private static readonly Guid Truck = Guid.Parse(
    "11111111-1111-4111-8111-111111111111"
  );

  private static RouteWorkSnapshot Load(
    Guid? leg = null,
    int stops = 2,
    int completed = 0,
    Guid? truck = null,
    Guid? stopTruck = null
  )
  {
    var id = Guid.NewGuid();
    var load = new Load
    {
      Id = id,
      TruckId = truck ?? Truck,
      Status = "assigned",
      Stops = Enumerable
        .Range(0, stops)
        .Select(i => new DispatchStop
        {
          Id = Guid.NewGuid(),
          DispatchId = id,
          Sequence = i + 1,
          Job = i == 0 ? "Pick Up" : "Drop Off",
          Latitude = 40 + i * 0.1m,
          Longitude = -100 + i * 0.1m,
          TruckId = stopTruck,
          DeliveredAt = i < completed ? DateTime.UtcNow : null,
          DepartedAt = i < completed ? DateTime.UtcNow : null,
        })
        .ToList(),
    };
    var snapshot = RouteWorkProjection.Capture(load);
    return leg is null ? snapshot : snapshot with { ExecutionLegId = leg };
  }

  [Fact]
  public void EveryFollowingLoadIsReadOnceForItsConnection()
  {
    var first = Load();
    var second = Load();
    var (connections, bases) = FuelHorizon.KeysToReadAhead(
      [first, second],
      Truck,
      1
    );
    Assert.Equal(
      [
        new FuelHorizon.SavedKey(first.Id, first.ExecutionLegId),
        new FuelHorizon.SavedKey(second.Id, second.ExecutionLegId),
      ],
      connections
    );
    // Both have more than one stop, so both need a base route as well.
    Assert.Equal(connections, bases);
  }

  [Fact]
  public void ALoadWithNothingLeftToDoIsNotReadAtAll()
  {
    var done = Load(stops: 2, completed: 2);
    var live = Load();
    var (connections, bases) = FuelHorizon.KeysToReadAhead(
      [done, live],
      Truck,
      1
    );
    Assert.DoesNotContain(
      new FuelHorizon.SavedKey(done.Id, done.ExecutionLegId),
      connections
    );
    Assert.Single(connections);
    Assert.Single(bases);
  }

  [Fact]
  public void ASingleStopLoadNeedsItsConnectionButNoBaseRoute()
  {
    var single = Load(stops: 1);
    var (connections, bases) = FuelHorizon.KeysToReadAhead([single], Truck, 1);
    Assert.Single(connections);
    Assert.Empty(bases);
  }

  [Fact]
  public void NothingIsReadPastALoadOnAnotherTruck()
  {
    var wrong = Load(truck: Guid.NewGuid());
    var after = Load();
    var (connections, _) = FuelHorizon.KeysToReadAhead(
      [wrong, after],
      Truck,
      1
    );
    // The loop throws on the first of these, so neither is ever read.
    Assert.Empty(connections);
  }

  [Fact]
  public void NothingIsReadPastALoadWhoseStopBelongsToAnotherTruck()
  {
    var mixed = Load(stopTruck: Guid.NewGuid());
    var (connections, _) = FuelHorizon.KeysToReadAhead([mixed], Truck, 1);
    Assert.Empty(connections);
  }

  [Fact]
  public void NothingIsReadPastTheItineraryLimit()
  {
    var first = Load(stops: 5);
    var overflowing = Load(stops: 5);
    var after = Load();
    // 34 already + 5 is 39 and fits; another 5 would be 44, past the forty
    // where the loop throws, so neither that load nor the one after is read.
    var (connections, _) = FuelHorizon.KeysToReadAhead(
      [first, overflowing, after],
      Truck,
      34
    );
    Assert.Single(connections);
    Assert.Equal(first.Id, connections[0].Dispatch);
  }
}
