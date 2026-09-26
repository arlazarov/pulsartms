using Bunit;
using Client.Models.DTO.Planning;
using Client.Pages.FleetMap;

namespace Client.Tests.Fleet;

// The fuel plan reads as the drive it belongs to: the load's remaining
// stops stand in the list, each fuel stop before the stop it is planned
// before, and the stops the truck has passed are not shown.
[Trait("Category", "Fleet")]
[Trait("Kind", "Component")]
public sealed class FleetFuelPlanTests
{
  [Fact]
  public void TheLoadsStopsStandBetweenTheFuelStops()
  {
    using var context = new BunitContext();
    var passed = Stop(1, "Pickup", "Origin Yard");
    var pickup = Stop(2, "Pickup", "Second Pickup");
    var delivery = Stop(3, "Delivery", "Callanan Industries");
    var plan = new FuelPlan
    {
      Stops =
      [
        Fuel(1, "LOVES #504", delivery.Id),
        Fuel(2, "PILOT #218", Guid.NewGuid()),
      ],
    };
    var component = context.Render<FleetFuelPlan>(p =>
      p.Add(x => x.Plan, plan)
        .Add(x => x.Stops, new[] { delivery, passed, pickup })
        .Add(x => x.NextStopId, pickup.Id)
    );
    var rows = component
      .FindAll(".fleet-fuel-plan__stops > li")
      .Select(li =>
        li.ClassList.Contains("fleet-fuel-plan__anchor")
          ? $"{li.QuerySelector("small")!.TextContent} {li.QuerySelector("strong")!.TextContent}"
          : $"fuel {li.QuerySelector("strong")!.TextContent}"
      )
      .ToList();
    // The fuel stop no remaining stop names comes first; the passed stop
    // is not shown.
    Assert.Equal(
      [
        "fuel PILOT #218",
        "Pickup Second Pickup",
        "fuel LOVES #504",
        "Delivery Callanan Industries",
      ],
      rows
    );
    Assert.Contains(
      "is-delivery",
      component.FindAll(".fleet-fuel-plan__anchor")[1].ClassName
    );
  }

  [Fact]
  public void WithTheNextLoadsHiddenOnlyThisDispatchsFuelStopsAreListed()
  {
    using var context = new BunitContext();
    var now = Guid.NewGuid();
    var delivery = Stop(1, "Delivery", "Here");
    // Booked to the next load: one on the way to this load's own stop,
    // which stays, and one past it, which is hidden.
    var plan = new FuelPlan
    {
      Stops =
      [
        Fuel(1, "LOVES #504", delivery.Id, now),
        Fuel(2, "PILOT #218", delivery.Id, Guid.NewGuid()),
        Fuel(3, "FLYING J #66", Guid.NewGuid(), Guid.NewGuid()),
      ],
    };
    var component = context.Render<FleetFuelPlan>(p =>
      p.Add(x => x.Plan, plan)
        .Add(x => x.Stops, new[] { delivery })
        .Add(x => x.OnlyDispatch, now)
    );
    var names = () =>
      component
        .FindAll(".fleet-fuel-plan__station > strong")
        .Select(x => x.TextContent)
        .ToList();
    Assert.Equal(["LOVES #504", "PILOT #218"], names());
    component.Render(p => p.Add(x => x.OnlyDispatch, null));
    Assert.Equal(["FLYING J #66", "LOVES #504", "PILOT #218"], names());
  }

  private static PlanStop Stop(int sequence, string job, string name) =>
    new(Guid.NewGuid(), name, "1 Main St, Albany, NY 12207, US", sequence, new(0, 0))
    {
      Job = job,
    };

  private static FuelPlanStop Fuel(
    int number,
    string name,
    Guid before,
    Guid? dispatch = null
  ) =>
    new()
    {
      DispatchId = dispatch ?? Guid.Empty,
      Number = number,
      VisitKey = $"visit-{number}",
      Name = name,
      Address = "1000 Highway 1, Dandridge, TN 37725, US",
      BeforeStopId = before,
      StationId = Guid.NewGuid(),
      ArrivalGallons = 100,
      BuyGallons = 25,
      YourPrice = 3.7,
      Currency = "USD",
      Unit = "US gal",
    };
}
