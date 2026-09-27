using Bunit;
using Client.Models.DTO.Dispatch;
using Client.Models.DTO.Fleet;
using Client.Models.DTO.Planning;
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
        .Add(x => x.Next, [Route(next)])
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
  public void DrawnLoadsOpenOnTheMapAndOthersLinkToTheirWorkspace()
  {
    using var context = new BunitContext();
    var current = Load(1409, "current");
    var next = Load(1410, "next");
    var later = Load(1411, "upcoming");
    DispatchResponse? chosen = null;
    var component = context.Render<FleetTripChain>(p =>
      p.Add(x => x.Truck, Truck)
        .Add(x => x.Loads, [current, next, later])
        .Add(x => x.CurrentId, current.Id)
        .Add(x => x.NextLoadsShown, true)
        .Add(x => x.Next, [Route(next)])
        .Add(x => x.Selected, load => chosen = load)
    );
    Assert.Equal(2, component.FindAll("button.fleet-trip-chain__card").Count);
    Assert.Equal(
      $"/dispatch/{later.Id}",
      component.Find("a.fleet-trip-chain__card").GetAttribute("href")
    );
    component.FindAll("button.fleet-trip-chain__card")[1].Click();
    Assert.Same(next, chosen);
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
    Assert.Empty(component.FindAll(".fleet-trip-chain__card"));
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

  private static NextLoadRoute Route(DispatchResponse load) =>
    new(load.Id, load.LoadNumber, "planned", [], []);
}
