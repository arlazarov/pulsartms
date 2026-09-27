using Bunit;
using Client.Models.DTO.Dispatch;
using Client.Models.DTO.Fleet;
using Client.Pages.FleetMap;

namespace Client.Tests.Fleet;

// The Futuristic trip chain names each load as the Dispatch board does,
// from the server's placement; it never places a load itself.
[Trait("Category", "Fleet")]
[Trait("Kind", "Component")]
public sealed class FleetTripChainTests
{
  private static readonly TruckLocationMapDto Truck = new()
  {
    TruckId = Guid.NewGuid(),
    UnitNumber = "54777",
  };

  [Fact]
  public void LoadsReadTheServerPhaseInTheBoardsOrder()
  {
    using var context = new BunitContext();
    var current = Load(1409, "current");
    var next = Load(1410, "next");
    var stale = Load(1411, "stale");
    var unknown = Load(1412, "unknown");
    var component = context.Render<FleetTripChain>(p =>
      p.Add(x => x.Truck, Truck)
        .Add(x => x.Loads, [current, next, stale, unknown])
        .Add(x => x.CurrentId, current.Id)
        .Add(x => x.NextLoadsShown, true)
    );
    Assert.Equal(
      ["Current", "Next", "Needs refresh"],
      component
        .FindAll(".fleet-trip-chain__phase")
        .Select(x => x.TextContent.Trim())
    );
    var links = component.FindAll(".fleet-trip-chain__link");
    Assert.Contains("is-current", links[0].ClassName);
    Assert.Contains("is-next", links[1].ClassName);
    // Neither a stale nor an unplaced load borrows a place's colour.
    Assert.Contains("is-unplaced", links[2].ClassName);
    Assert.Contains("is-unplaced", links[3].ClassName);
  }

  [Fact]
  public void EachTripOffersItsStopsWithTheMapsLabels()
  {
    using var context = new BunitContext();
    var current = Load(1409, "current");
    var next = Load(1410, "next", deliveries: 2);
    (DispatchResponse Load, Guid Stop)? chosen = null;
    DispatchResponse? trip = null;
    var component = context.Render<FleetTripChain>(p =>
      p.Add(x => x.Truck, Truck)
        .Add(x => x.Loads, [current, next])
        .Add(x => x.CurrentId, current.Id)
        .Add(x => x.SelectedTrip, next)
        .Add(x => x.FocusedStopId, next.Stops[2].Id)
        .Add(x => x.Selected, load => trip = load)
        .Add(x => x.StopSelected, value => chosen = value)
    );
    var links = component.FindAll(".fleet-trip-chain__link");
    Assert.Equal(
      ["P", "D"],
      links[0]
        .QuerySelectorAll(".fleet-trip-chain__stop")
        .Select(x => x.TextContent.Trim())
    );
    // The trip's own deliveries, never its place in the chain.
    Assert.Equal(
      ["P", "D1", "D2"],
      links[1]
        .QuerySelectorAll(".fleet-trip-chain__stop")
        .Select(x => x.TextContent.Trim())
    );
    Assert.Contains("is-selected", links[1].ClassName);
    Assert.Equal(
      "true",
      links[1]
        .QuerySelectorAll(".fleet-trip-chain__stop")[2]
        .GetAttribute("aria-pressed")
    );

    component.FindAll(".fleet-trip-chain__stop")[3].Click();
    Assert.Same(next, chosen?.Load);
    Assert.Equal(next.Stops[1].Id, chosen?.Stop);
    component.FindAll(".fleet-trip-chain__trip")[0].Click();
    Assert.Same(current, trip);
  }

  [Fact]
  public void TheServersConflictIsSaidOnTheLoad()
  {
    using var context = new BunitContext();
    var passed = Load(1408, "earlier");
    passed.WorkConflict = "route_passed_not_delivered";
    var component = context.Render<FleetTripChain>(p =>
      p.Add(x => x.Truck, Truck).Add(x => x.Loads, [passed])
    );
    Assert.Equal(
      "Route passed · not delivered",
      component.Find(".fleet-trip-chain__conflict").TextContent.Trim()
    );
    Assert.Empty(component.FindAll(".fleet-trip-chain__phase"));
  }

  [Fact]
  public void AFailedReadSaysSoInsteadOfAnEmptyChain()
  {
    using var context = new BunitContext();
    var component = context.Render<FleetTripChain>(p =>
      p.Add(x => x.Truck, Truck).Add(x => x.Failed, true)
    );
    Assert.Contains("could not be read", component.Markup);
    Assert.Empty(component.FindAll(".fleet-trip-chain__trip"));
  }

  private static DispatchResponse Load(int number, string phase) =>
    new()
    {
      Id = Guid.NewGuid(),
      LoadNumber = number,
      WorkPhase = phase,
      Stops =
      [
        new()
        {
          Sequence = 1,
          City = "Nashville",
          Province = "TN",
        },
        new()
        {
          Sequence = 2,
          City = "Knoxville",
          Province = "TN",
        },
      ],
    };
}
