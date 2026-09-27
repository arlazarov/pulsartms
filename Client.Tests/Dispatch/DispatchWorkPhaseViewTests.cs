using Bunit;
using Client.Models.DTO.Dispatch;
using Client.Pages.Dispatch;
using Client.Shared.Dispatch;
using Microsoft.Extensions.DependencyInjection;

namespace Client.Tests.Dispatch;

// Stage 3a of docs/architecture/current-work.md: every dispatch view names
// the place the server gave each load and shows its conflict, and none
// works a place out from the board's order. The table and the papers get
// the phase the board list gives them (DispatchWorkPhase.Label).
[Trait("Category", "Dispatch")]
[Trait("Kind", "Component")]
public sealed class DispatchWorkPhaseViewTests
{
  // The AMF1395 shape: planning passed the first load's route, execution
  // has not completed it. Neither view calls it awaiting pickup.
  [Fact]
  public void APassedRouteNotDeliveredReadsAsItsConflictInEveryView()
  {
    using var context = Context();
    var load = DispatchFinancialViewsTests.Load(1395);
    foreach (var stop in load.Stops)
    {
      stop.PickedUpAt = null;
      stop.IsCompleted = false;
    }
    load.Status = "assigned";
    load.WorkPhase = "earlier";
    load.WorkConflict = "route_passed_not_delivered";
    var truck = Truck(load);

    var table = context.Render<DispatchTable>(p =>
      p.Add(view => view.Trucks, [truck])
        .Add(view => view.LoadPhase, (_, row) => DispatchWorkPhase.Label(row))
    );
    var papers = context.Render<DispatchPapers>(p =>
      p.Add(view => view.Trucks, [truck])
        .Add(view => view.LoadPhase, (_, row) => DispatchWorkPhase.Label(row))
    );

    var status = table.Find(".dispatch-table__status");
    Assert.Equal("Route passed · not delivered", status.TextContent);
    Assert.Contains("is-conflict", status.ClassList);
    Assert.DoesNotContain("Awaiting pickup", table.Markup);
    var tab = papers.Find(".dispatch-paper__tab-status");
    Assert.Equal("Route passed · not delivered", tab.TextContent);
    Assert.Contains("is-conflict", tab.ClassList);
    // Not filed under "Awaiting pickup" either.
    Assert.Empty(
      papers.FindAll(".dispatch-paper-column--0 .dispatch-paper-tab-entry")
    );
  }

  // A row read at another assignment revision than the planning inputs
  // is stale and says it is updating; a load the inputs do not hold, or
  // hold without a place, claims no phase. None is given a planned phase.
  [Theory]
  [InlineData("stale", "Updating")]
  [InlineData("unknown", "")]
  [InlineData("unplaced", "Planned")]
  public void AStaleOrUnknownPlaceIsNotReadAsPlanned(
    string placement,
    string shown
  )
  {
    using var context = Context();
    var load = DispatchFinancialViewsTests.Load(1412);
    load.Status = "planned";
    load.WorkPhase = placement;
    var table = context.Render<DispatchTable>(p =>
      p.Add(view => view.Trucks, [Truck(load)])
        .Add(view => view.LoadPhase, (_, row) => DispatchWorkPhase.Label(row))
    );

    // The phase badge; the status beside it is the load's own ("Planned").
    Assert.Equal(
      shown == "Updating" ? ["Updating"] : [],
      table.FindAll(".dispatch-table__phase").Select(x => x.TextContent)
    );
    Assert.Equal("Planned", table.Find(".dispatch-table__status").TextContent);
  }

  // The board list's cards on a board whose rows come in another order
  // than the truck's work: each card names its own place, whatever its
  // position - the next load listed first is still next, and a lone page
  // showing only a later load still calls it upcoming.
  [Fact]
  public void CardsNameTheirPlaceWhateverTheirPosition()
  {
    using var context = Context();
    var current = DispatchFinancialViewsTests.Load(1412);
    current.WorkPhase = "current";
    var next = DispatchFinancialViewsTests.Load(1413);
    next.WorkPhase = "next";
    var later = DispatchFinancialViewsTests.Load(1414);
    later.WorkPhase = "upcoming";

    string Phase(DispatchResponse load) =>
      context
        .Render<DispatchLoadCard>(p => p.Add(card => card.Load, load))
        .FindAll(".dispatch-load__phase")
        .SingleOrDefault()
        ?.TextContent ?? "";

    Assert.Equal(
      ["Next", "Current", "Upcoming"],
      new[] { next, current, later }.Select(Phase)
    );
    Assert.Equal("Upcoming", Phase(later));
  }

  private static TruckDispatchBoardResponse Truck(DispatchResponse load) =>
    new()
    {
      Key = "11006",
      TruckId = load.TruckId ?? Guid.NewGuid(),
      TruckNumber = "11006",
      Dispatches = [load],
    };

  private static BunitContext Context()
  {
    var context = new BunitContext();
    context.Services.AddSingleton(TimeProvider.System);
    context.JSInterop.SetupModule("./js/generated/shared/loadDialog.js").Mode =
      JSRuntimeMode.Loose;
    return context;
  }
}
