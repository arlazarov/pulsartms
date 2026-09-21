using Bunit;
using Client.Models.DTO.Dispatch;
using Client.Pages.Dispatch;
using Client.Shared;
using Client.Shared.Dispatch;
using Client.Shared.Dispatch.DispatchLoadDialog;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace Client.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Component")]
public sealed class DispatchTableInteractionTests
{
  [Fact]
  public async Task OpenLoadHasNativeUrlAndOrdinaryRowClickNavigates()
  {
    await using var context = Context();
    var load = DispatchFinancialViewsTests.Load();
    var truck = new TruckDispatchBoardResponse
    {
      Key = "truck",
      Dispatches = [load],
    };
    var table = context.Render<DispatchTable>(p =>
      p.Add(view => view.Trucks, [truck])
        .Add(view => view.LoadPhase, (_, _) => "Current")
    );
    var open = table.Find("a.dispatch-table__open");
    Assert.Equal($"/dispatch/{load.Id}", open.GetAttribute("href"));
    Assert.Null(open.GetAttribute("aria-haspopup"));
    Assert.StartsWith("Open load ", open.GetAttribute("aria-label"));
    Assert.Empty(table.FindAll("dialog, details"));
    await table.Find("tr.dispatch-table__row").ClickAsync(new MouseEventArgs());
    Assert.EndsWith(
      $"/dispatch/{load.Id}",
      context.Services.GetRequiredService<NavigationManager>().Uri
    );
    Assert.Empty(table.FindAll("dialog"));
  }

  [Fact]
  public async Task MapNavigationAndModifiedRowClicksDoNotOpenDialog()
  {
    await using var context = Context();
    var load = DispatchFinancialViewsTests.Load();
    var table = context.Render<DispatchTable>(p =>
      p.Add(
        view => view.Trucks,
        [new TruckDispatchBoardResponse { Key = "truck", Dispatches = [load] }]
      )
    );
    var map = table.Find("a.dispatch-table__map");
    Assert.Contains($"dispatchId={load.Id}", map.GetAttribute("href"));
    Assert.Equal("a", map.LocalName);
    Assert.DoesNotContain(
      context.JSInterop.Invocations,
      invocation => invocation.Identifier == "import"
    );
    Assert.Empty(table.FindAll("dialog"));
    foreach (
      var args in new[]
      {
        new MouseEventArgs { CtrlKey = true },
        new MouseEventArgs { MetaKey = true },
        new MouseEventArgs { ShiftKey = true },
        new MouseEventArgs { AltKey = true },
        new MouseEventArgs { Button = 1 },
      }
    )
      await table.Find("tr.dispatch-table__row").ClickAsync(args);
    Assert.Empty(table.FindAll("dialog"));
    Assert.Equal(
      "http://localhost/",
      context.Services.GetRequiredService<NavigationManager>().Uri
    );
  }

  [Fact]
  public async Task LinksFollowRefreshedRowsWithoutRetainingRemovedLoads()
  {
    await using var context = Context();
    var first = DispatchFinancialViewsTests.Load();
    var table = context.Render<DispatchTable>(p =>
      p.Add(
        view => view.Trucks,
        [new TruckDispatchBoardResponse { Key = "truck", Dispatches = [first] }]
      )
    );
    Assert.Equal(
      $"/dispatch/{first.Id}",
      table.Find(".dispatch-table__open").GetAttribute("href")
    );
    var updated = DispatchFinancialViewsTests.Load();
    updated.Id = first.Id;
    updated.CustomerName = "Updated customer";
    var trucks = new[]
    {
      new TruckDispatchBoardResponse { Key = "truck", Dispatches = [updated] },
    };
    table.Render(p => p.Add(view => view.Trucks, trucks));
    Assert.Contains("Updated customer", table.Markup);
    Assert.Equal(
      $"/dispatch/{first.Id}",
      table.Find(".dispatch-table__open").GetAttribute("href")
    );
    table.Render(p => p.Add(view => view.Trucks, []));
    Assert.Empty(table.FindAll(".dispatch-table__open, dialog"));
    table.Render(p =>
      p.Add(view => view.Trucks, trucks).Add(view => view.Completed, true)
    );
    Assert.Equal(
      $"/dispatch/{first.Id}",
      table.Find(".dispatch-table__open").GetAttribute("href")
    );
    Assert.Empty(table.FindAll("dialog"));
  }

  [Fact]
  public async Task SummaryAdvancesPastCompletedStopsAndShowsLastVisitsWhenCompleted()
  {
    await using var context = Context();
    var load = DispatchFinancialViewsTests.Load();
    load.Stops.Add(
      new()
      {
        Id = Guid.NewGuid(),
        Sequence = 3,
        Job = "Pickup",
        City = "Second origin",
      }
    );
    load.Stops.Add(
      new()
      {
        Id = Guid.NewGuid(),
        Sequence = 4,
        Job = "Delivery",
        City = "Second destination",
      }
    );
    load.Stops.Reverse();
    var table = context.Render<DispatchTable>(p =>
      p.Add(
        view => view.Trucks,
        [new TruckDispatchBoardResponse { Key = "truck", Dispatches = [load] }]
      )
    );
    Assert.Equal(
      ["Second origin"],
      table
        .FindAll(
          ".dispatch-table__stop.is-pickup .dispatch-table__stop-entry > strong"
        )
        .Select(e => e.TextContent)
    );
    Assert.Equal(
      ["Destination"],
      table
        .FindAll(
          ".dispatch-table__stop.is-delivery .dispatch-table__stop-entry > strong"
        )
        .Select(e => e.TextContent)
    );
    Assert.Empty(table.FindAll(".dispatch-table__stop-completed"));
    Assert.Contains("2 pickups · 1 completed", table.Markup);
    Assert.Empty(table.FindAll("details"));
    Assert.Equal(8, table.FindAll("tbody tr:first-child td").Count);
    load.Status = "completed";
    load.Completed = true;
    table.Render();
    Assert.Equal(2, table.FindAll(".dispatch-table__stop-completed").Count);
    Assert.Equal(
      ["Second origin", "Second destination"],
      table
        .FindAll(".dispatch-table__stop-entry > strong")
        .Select(e => e.TextContent)
    );
    Assert.Equal(
      ["Last · Pickup", "Last · Delivery"],
      table.FindAll(".dispatch-table__stop-context").Select(e => e.TextContent)
    );
    Assert.Equal(
      ["2 pickups · 2 completed", "2 deliveries · 2 completed"],
      table.FindAll(".dispatch-table__stops-summary").Select(e => e.TextContent)
    );
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task LoadedAfterStateIsOmittedFromTableWithoutChangingStopData(
    bool completed
  )
  {
    await using var context = Context();
    var load = DispatchFinancialViewsTests.Load();
    var pickup = load.Stops[0];
    pickup.StateAfter = "Loaded";
    pickup.PickedUpAt = completed ? DateTime.UtcNow.AddHours(-1) : null;
    pickup.IsCompleted = completed;
    pickup.Address = "1 Arizona Way";
    pickup.ScheduledDate = new(2026, 9, 12);
    pickup.ScheduledTime = new(12, 0);
    var table = context.Render<DispatchTable>(p =>
      p.Add(
        view => view.Trucks,
        [new TruckDispatchBoardResponse { Key = "truck", Dispatches = [load] }]
      )
    );

    var cell = table.Find(".dispatch-table__stop.is-pickup");
    Assert.DoesNotContain("After:", cell.TextContent);
    Assert.Contains("Origin", cell.TextContent);
    Assert.Contains("1 Arizona Way", cell.TextContent);
    Assert.Contains("12:00 PM", cell.TextContent);
    Assert.Equal(
      completed ? 1 : 0,
      cell.QuerySelectorAll(".dispatch-table__stop-completed").Length
    );
    Assert.Equal("Loaded", pickup.StateAfter);

    Assert.Equal(
      $"/dispatch/{load.Id}",
      table.Find(".dispatch-table__open").GetAttribute("href")
    );
    var dialog = context.Render<DispatchLoadDialog>(p =>
      p.Add(x => x.Load, load)
    );
    Assert.Same(load, dialog.Instance.Load);
    Assert.Equal("Loaded", dialog.Instance.Load.Stops[0].StateAfter);
  }

  private static BunitContext Context()
  {
    var context = new BunitContext();
    context.Services.AddSingleton(TimeProvider.System);
    context.JSInterop.SetupModule("./js/generated/shared/loadDialog.js").Mode =
      JSRuntimeMode.Loose;
    return context;
  }
}
