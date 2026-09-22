using System.Text.Json;
using Domain.Entities.Dispatch;
using Domain.Entities.Fleet;
using Domain.Rules;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Eta;

public sealed partial class EtaChainInputTests
{
  [Fact]
  public async Task MultipleTrucksKeepIndividualHashesAndHistoryBatches()
  {
    await using var fixture = await Fixture.CreateAsync();
    var truck = new Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = "eta-batch-second",
      UnitNumber = "ETA2",
      IsActive = true,
    };
    fixture.Db.Trucks.Add(truck);
    foreach (var source in new[] { fixture.Current, fixture.Next })
      fixture.Db.Dispatches.Add(
        new Load
        {
          Id = Guid.NewGuid(),
          LoadNumber = source.LoadNumber + 100,
          TruckId = truck.Id,
          Status = source.Status,
          ShipDate = source.ShipDate,
          DeliveryDate = source.DeliveryDate,
          Stops = source
            .Stops.Select(stop => new DispatchStop
            {
              Id = Guid.NewGuid(),
              TruckId = truck.Id,
              Sequence = stop.Sequence,
              Job = stop.Job,
              ScheduledDate = stop.ScheduledDate,
              ScheduledTime = stop.ScheduledTime,
              Latitude = stop.Latitude,
              Longitude = stop.Longitude,
            })
            .ToList(),
        }
      );
    await fixture.Db.SaveChangesAsync();
    var expected = new Dictionary<Guid, string>();
    foreach (var id in new[] { fixture.Truck.Id, truck.Id })
    {
      var description = await fixture.Services.EtaInputs.DescribeAsync(
        id,
        default
      );
      Assert.NotNull(description);
      expected[id] = JsonSerializer.Serialize(
        description with
        {
          Itinerary = description.Itinerary with { AsOf = default },
        },
        RoutingJson.Options
      );
    }
    var actual = await fixture.Services.EtaInputs.DescribeManyAsync(
      [new() { TruckId = fixture.Truck.Id }, new() { TruckId = truck.Id }],
      default
    );
    Assert.Equal(2, actual.Count);
    foreach (var (id, description) in actual)
      Assert.Equal(
        expected[id],
        JsonSerializer.Serialize(
          description with
          {
            Itinerary = description.Itinerary with { AsOf = default },
          },
          RoutingJson.Options
        )
      );
  }
}
