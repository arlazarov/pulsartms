using Bunit;
using Client.Models.DTO.Dispatch;
using Client.Pages.Dispatch;
using Client.Shared.Dispatch.DispatchLoadDialog;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace Client.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Component")]
public sealed class DispatchFinancialViewsTests
{
  [Fact]
  public void AllViewsFormatServerFinancialsAndPreserveCompletedPickup()
  {
    using var context = new BunitContext();
    context.Services.AddSingleton(TimeProvider.System);
    context.JSInterop.Mode = JSRuntimeMode.Loose;
    context.JSInterop.SetupModule("./js/generated/shared/loadDialog.js").Mode =
      JSRuntimeMode.Loose;
    var load = Load();
    var trucks = new[]
    {
      new TruckDispatchBoardResponse { Key = "historic", Dispatches = [load] },
    };
    var cards = context.Render<DispatchLoadCard>(parameters =>
      parameters.Add(card => card.Load, load)
    );
    Assert.Empty(
      cards.FindAll(".dispatch-load__metrics, .dispatch-paper__financials")
    );
    Assert.Equal(2, cards.FindAll(".dispatch-load__stop").Count);
    Assert.NotNull(
      cards
        .Find(".dispatch-load__stop")
        .QuerySelector("[aria-label='Completed']")
    );
    Assert.Equal(
      $"/dispatch/{load.Id}",
      cards.Find(".dispatch-load__details").GetAttribute("href")
    );
    var details = context.Render<DispatchLoadDialog>(p =>
      p.Add(x => x.Load, load)
    );
    AssertFinancials(details.Find(".dispatch-paper__financials").TextContent);

    var table = context.Render<DispatchTable>(parameters =>
      parameters.Add(view => view.Trucks, trucks)
    );
    AssertFinancials(table.Markup);
    Assert.Contains("Pickup", table.Markup);
    Assert.Contains("Origin", table.Markup);
    Assert.Contains("Destination", table.Markup);
    Assert.Single(table.FindAll(".dispatch-table__stop-completed"));

    var papers = context.Render<DispatchPapers>(parameters =>
      parameters.Add(view => view.Trucks, trucks)
    );
    Assert.Contains(
      "1,000.00 CAD",
      papers.Find(".dispatch-paper__tab-financials").TextContent
    );
    Assert.Equal(
      $"/dispatch/{load.Id}",
      papers.Find(".dispatch-paper__tab").GetAttribute("href")
    );
    Assert.Empty(papers.FindAll("dialog"));
    AssertFinancials(details.Find(".dispatch-paper__financials").TextContent);
    Assert.Equal(2, details.FindAll(".dispatch-paper__stops li").Count);
    Assert.Single(details.FindAll(".dispatch-load__completed"));
  }

  [Fact]
  public void TableGroupsHistoricalTruckDriverAndKeepsEightFinancialColumns()
  {
    using var context = new BunitContext();
    var load = Load();
    load.Status = "completed";
    load.Completed = true;
    load.OrderNumber = "ORDER-19";
    load.Stops[0].Address = "123 Origin Street";
    var trucks = new[]
    {
      new TruckDispatchBoardResponse
      {
        Key = "truck",
        TruckNumber = "Live Truck",
        DriverName = "Live Driver",
        TrailerNumber = "Live Trailer",
        Dispatches = [load],
      },
    };
    var table = context.Render<DispatchTable>(parameters =>
      parameters.Add(view => view.Trucks, trucks)
    );

    Assert.Equal(
      [
        "Load / status",
        "Truck / driver",
        "Pickup",
        "Delivery",
        "Distance",
        "Rate",
        "Loaded RPM",
        "Total RPM",
      ],
      table.FindAll("thead th").Select(cell => cell.TextContent).ToArray()
    );
    Assert.Equal(8, table.FindAll("tbody tr:first-child td").Count);
    var equipment = table.Find(".dispatch-table__equipment").TextContent;
    foreach (
      var text in new[] { "54777", "Historic Driver", "Trailer ARCHIVE-1" }
    )
      Assert.Contains(text, equipment);
    Assert.Empty(table.FindAll("details"));
    Assert.DoesNotContain("Live Driver", table.Markup);
    Assert.Contains(
      "ORDER-19",
      table.Find(".dispatch-table__load").TextContent
    );
    Assert.Contains(
      "Customer",
      table.Find(".dispatch-table__load").TextContent
    );
    Assert.Contains(
      "123 Origin Street",
      table.Find(".dispatch-table__stop").TextContent
    );
    Assert.Equal(
      "Completed",
      table.Find(".dispatch-table__status").TextContent
    );
    Assert.Equal(2, table.FindAll(".dispatch-table__stop-completed").Count);
    Assert.Equal(
      ["1,000.00 CAD", "17.23 CAD", "4.56 CAD"],
      table
        .FindAll("tbody .dispatch-table__money strong")
        .Select(cell => cell.TextContent)
        .ToArray()
    );
    Assert.Single(table.FindAll(".dispatch-table__map"));
    Assert.Single(table.FindAll(".dispatch-table__truck .dispatch-table__map"));
    Assert.Empty(
      table.FindAll(".dispatch-table__equipment > .dispatch-table__map")
    );
  }

  [Fact]
  public void TableAndPapersDisplayCompleteAppointmentWindows()
  {
    using var context = new BunitContext();
    context.Services.AddSingleton(TimeProvider.System);
    context.JSInterop.Mode = JSRuntimeMode.Loose;
    context.JSInterop.SetupModule("./js/generated/shared/loadDialog.js").Mode =
      JSRuntimeMode.Loose;
    var load = Load();
    load.Stops[0].IsWindow = true;
    load.Stops[0].ScheduledDate = new(2026, 9, 9);
    load.Stops[0].ScheduledTime = new(7, 0);
    load.Stops[0].ScheduledDate2 = new(2026, 9, 9);
    load.Stops[0].ScheduledTime2 = new(14, 0);
    load.Stops[1].IsWindow = true;
    load.Stops[1].ScheduledDate = new(2026, 9, 10);
    load.Stops[1].ScheduledTime = new(23, 0);
    load.Stops[1].ScheduledDate2 = new(2026, 9, 11);
    load.Stops[1].ScheduledTime2 = new(5, 0);
    var trucks = new[]
    {
      new TruckDispatchBoardResponse { Key = "truck", Dispatches = [load] },
    };
    var expected = new[]
    {
      "Sep 9 · 07:00 AM – 02:00 PM",
      "Sep 10 · 11:00 PM – Sep 11 · 05:00 AM",
    };
    var table = context.Render<DispatchTable>(parameters =>
      parameters.Add(view => view.Trucks, trucks)
    );
    Assert.Equal(
      expected,
      table
        .FindAll(".dispatch-table__schedule")
        .Select(schedule => schedule.TextContent)
        .ToArray()
    );
    var papers = context.Render<DispatchPapers>(parameters =>
      parameters.Add(view => view.Trucks, trucks)
    );
    Assert.Equal(
      $"/dispatch/{load.Id}",
      papers.Find(".dispatch-paper__tab").GetAttribute("href")
    );
    var details = context.Render<DispatchLoadDialog>(p =>
      p.Add(x => x.Load, load)
    );
    foreach (var schedule in expected)
      Assert.Contains(
        schedule,
        details.Find(".dispatch-paper__stops").TextContent
      );
  }

  [Fact]
  public void PapersKeepNativeLoadLinksWhenTheirFolderChanges()
  {
    using var context = new BunitContext();
    context.Services.AddSingleton(TimeProvider.System);
    var first = Load(1375);
    var second = Load(1373);
    second.Status = "planned";
    second.Stops[0].PickedUpAt = null;
    second.Stops[0].IsCompleted = true;
    var trucks = new[]
    {
      new TruckDispatchBoardResponse
      {
        Key = "truck",
        Dispatches = [first, second],
      },
    };
    var papers = context.Render<DispatchPapers>(p =>
      p.Add(x => x.Trucks, trucks)
    );
    Assert.Equal(3, papers.FindAll(".dispatch-paper-column").Count);
    Assert.Empty(papers.FindAll("dialog"));
    var before = papers
      .FindAll("a.dispatch-paper__tab")
      .Single(tab =>
        tab.TextContent.Contains("1373", StringComparison.Ordinal)
      );
    Assert.Equal($"/dispatch/{second.Id}", before.GetAttribute("href"));
    Assert.Null(before.GetAttribute("aria-expanded"));
    Assert.All(
      papers.FindAll(".dispatch-paper__tab-content"),
      content =>
        Assert.Equal(
          2,
          content.QuerySelectorAll(".dispatch-paper__tab-line").Length
        )
    );
    Assert.All(
      papers.FindAll(".dispatch-paper__tab-content"),
      content =>
        Assert.NotNull(content.QuerySelector(".dispatch-paper__tab-schedule"))
    );

    second.Status = "in_transit";
    second.Stops[0].PickedUpAt = DateTime.UtcNow;
    second.Stops[0].IsCompleted = true;
    papers.Render(p => p.Add(x => x.Trucks, trucks));
    var after = papers
      .FindAll(".dispatch-paper-column--1 a")
      .Single(tab =>
        tab.TextContent.Contains("1373", StringComparison.Ordinal)
      );
    Assert.Equal($"/dispatch/{second.Id}", after.GetAttribute("href"));
    Assert.Contains("In transit", after.TextContent);
    Assert.Empty(papers.FindAll("dialog"));
    Assert.Empty(context.JSInterop.Invocations);
  }

  [Fact]
  public void PapersRemoveObsoleteLoadLinksWhenSearchPageChanges()
  {
    using var context = new BunitContext();
    context.Services.AddSingleton(TimeProvider.System);
    var load = Load();
    var papers = context.Render<DispatchPapers>(p =>
      p.Add(
        x => x.Trucks,
        [new TruckDispatchBoardResponse { Key = "truck", Dispatches = [load] }]
      )
    );
    Assert.Equal(
      $"/dispatch/{load.Id}",
      papers.Find(".dispatch-paper__tab").GetAttribute("href")
    );
    papers.Render(p => p.Add(x => x.Trucks, []));
    Assert.Empty(papers.FindAll("dialog, .dispatch-paper__tab"));
    Assert.Empty(context.JSInterop.Invocations);
  }

  [Fact]
  public void TableAndPapersKeepBoardPhaseSeparateFromActualStatusAndArchiveNeverClaimsCurrent()
  {
    using var context = new BunitContext();
    context.Services.AddSingleton(TimeProvider.System);
    context.JSInterop.Mode = JSRuntimeMode.Loose;
    context.JSInterop.SetupModule("./js/generated/shared/loadDialog.js").Mode =
      JSRuntimeMode.Loose;
    var current = Load(1375);
    var next = Load(1373);
    next.Status = "planned";
    next.Stops[0].PickedUpAt = null;
    next.Stops[0].IsCompleted = true;
    var trucks = new[]
    {
      new TruckDispatchBoardResponse
      {
        Key = "truck",
        Dispatches = [current, next],
      },
    };
    string Phase(TruckDispatchBoardResponse truck, DispatchResponse load) =>
      truck.Dispatches[0].Id == load.Id ? "Current" : "Next";
    var table = context.Render<DispatchTable>(parameters =>
      parameters
        .Add(view => view.Trucks, trucks)
        .Add(view => view.LoadPhase, Phase)
    );
    Assert.Equal(
      ["Current", "Next"],
      table
        .FindAll(".dispatch-table__phase")
        .Select(badge => badge.TextContent)
        .ToArray()
    );
    Assert.Equal(
      ["In transit", "Planned"],
      table
        .FindAll(".dispatch-table__status")
        .Select(badge => badge.TextContent)
        .ToArray()
    );
    Assert.Single(table.FindAll("tr.is-current"));
    Assert.Single(table.FindAll("tr.is-next"));
    Assert.Contains(
      "500\u00a0mi",
      table.Find(".dispatch-table__mileage-values").TextContent
    );
    Assert.Contains(
      "550\u00a0mi",
      table.Find(".dispatch-table__mileage-values").TextContent
    );

    var papers = context.Render<DispatchPapers>(parameters =>
      parameters
        .Add(view => view.Trucks, trucks)
        .Add(view => view.LoadPhase, Phase)
    );
    var nextTab = papers
      .FindAll("a.dispatch-paper__tab")
      .Single(tab =>
        tab.TextContent.Contains("1373", StringComparison.Ordinal)
      );
    Assert.Equal($"/dispatch/{next.Id}", nextTab.GetAttribute("href"));
    Assert.Equal(
      "Next",
      nextTab.QuerySelector(".dispatch-paper__phase")!.TextContent.Trim()
    );
    Assert.Equal(
      "Planned",
      nextTab.QuerySelector(".dispatch-paper__tab-status")!.TextContent
    );
    var details = context.Render<DispatchLoadDialog>(p =>
      p.Add(x => x.Load, next).Add(x => x.Phase, "Next")
    );
    Assert.Equal(
      "Loaded distance",
      details
        .Find(".dispatch-paper__financials > div:first-child dt")
        .TextContent
    );
    Assert.Contains(
      "550\u00a0mi · 885\u00a0km total",
      papers.Find(".dispatch-paper__tab-miles").TextContent
    );

    current.Status = "completed";
    current.Completed = true;
    trucks[0].Dispatches = [current];
    table.Render(parameters =>
      parameters
        .Add(view => view.Trucks, trucks)
        .Add(view => view.Completed, true)
    );
    Assert.Empty(
      table.FindAll(".dispatch-table__phase, tr.is-current, tr.is-next")
    );
    Assert.Equal(
      "Completed",
      table.Find(".dispatch-table__status").TextContent
    );
    papers.Render(parameters =>
      parameters
        .Add(view => view.Trucks, trucks)
        .Add(view => view.Completed, true)
    );
    Assert.Equal(
      $"/dispatch/{current.Id}",
      papers.Find(".dispatch-paper__tab").GetAttribute("href")
    );
    Assert.Empty(papers.FindAll(".dispatch-paper__phase"));
    Assert.Equal(
      "Completed",
      papers.Find(".dispatch-paper__tab-status").TextContent
    );
  }

  [Fact]
  public void CompletedLoadNeverShowsCurrentNextOrLiveEtaEvenWithoutStopTimestamps()
  {
    using var context = new BunitContext();
    context.Services.AddSingleton(TimeProvider.System);
    var load = Load();
    load.Status = "completed";
    load.Completed = true;
    foreach (var stop in load.Stops)
    {
      stop.PickedUpAt = null;
      stop.IsCompleted = false;
    }
    var card = context.Render<DispatchLoadCard>(parameters =>
      parameters.Add(view => view.Load, load).Add(view => view.Current, true)
    );
    Assert.Equal("Completed", card.Find(".dispatch-load__phase").TextContent);
    Assert.Equal("Completed", card.Find(".dispatch-load__status").TextContent);
    Assert.Equal(2, card.FindAll(".dispatch-load__stop--completed").Count);
    Assert.Empty(card.FindAll(".dispatch-load--current"));
    Assert.Empty(
      card.FindAll(
        ".arrival-estimate, .dispatch-cycle-forecast, .dispatch-load__eta-missing"
      )
    );
  }

  internal static DispatchResponse Load(int number = 1375) =>
    new()
    {
      Id = Guid.NewGuid(),
      TruckId = Guid.NewGuid(),
      LoadNumber = number,
      TruckNumber = "54777",
      Status = "assigned",
      DriverName = "Historic Driver",
      TrailerNumber = "ARCHIVE-1",
      CustomerName = "Customer",
      Price = 1000,
      Currency = "cad",
      LoadedMiles = 500,
      EmptyMiles = 50,
      TotalMiles = 550,
      LoadedRatePerMile = 17.23m,
      TotalRatePerMile = 4.56m,
      Stops =
      [
        new()
        {
          Id = Guid.NewGuid(),
          Sequence = 1,
          Job = "Pick Up",
          City = "Origin",
          PickedUpAt = DateTime.UtcNow.AddDays(-2),
          IsCompleted = true,
        },
        new()
        {
          Id = Guid.NewGuid(),
          Sequence = 2,
          Job = "Drop Off",
          City = "Destination",
        },
      ],
    };

  private static void AssertFinancials(string text)
  {
    foreach (
      var value in new[]
      {
        "1,000.00 CAD",
        "17.23 CAD",
        "4.56 CAD",
        "500\u00a0mi",
        "50\u00a0mi",
        "550\u00a0mi",
      }
    )
      Assert.Contains(value, text);
  }
}
