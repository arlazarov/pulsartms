using AngleSharp.Dom;
using Bunit;
using Client.Models.DTO.Dispatch;
using Client.Pages.Dispatch;
using Client.Shared.Dispatch.DispatchLoadDialog;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace Client.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Component")]
public sealed class DispatchMultiStopPresentationTests
{
  [Fact]
  public void CardAndSharedDialogKeepFiveStopsAndDistinguishThreeVisitsToTheSameFacility()
  {
    using var context = Context();
    var load = Load();
    var expected = load.Stops.ToArray();
    load.Stops.Reverse();
    var card = context.Render<DispatchLoadCard>(parameters =>
      parameters
        .Add(component => component.Load, load)
        .Add(component => component.Current, true)
        .Add(component => component.RemainingMiles, 1200)
        .Add(component => component.FuelStopCount, 2)
    );

    AssertStops(card.Find(".dispatch-load"), expected);
    Assert.Equal(5, card.FindAll(".dispatch-load__stop--summary").Count);
    Assert.Empty(card.FindAll(".dispatch-load__facility"));
    Assert.Equal(
      "5 stops · 4 pickups · 1 delivery",
      card.Find(".dispatch-load__stop-summary").TextContent
    );
    Assert.Contains(
      "Fuel stops 2",
      card.Find(".dispatch-load__footer").TextContent
    );
    Assert.Equal(
      $"/dispatch/{load.Id}",
      card.Find(".dispatch-load__details").GetAttribute("href")
    );
    Assert.Empty(card.FindAll("dialog"));
    var dialog = context.Render<DispatchLoadDialog>(p =>
      p.Add(x => x.Load, load)
    );
    Assert.Same(load, dialog.Instance.Load);
    AssertStops(dialog.Find("dialog"), expected);
    Assert.Empty(dialog.FindAll("dialog .dispatch-load__stop--summary"));
    Assert.Equal(5, dialog.FindAll("dialog .dispatch-load__facility").Count);
    Assert.Equal(
      "5 stops · 4 pickups · 1 delivery",
      dialog.Find("dialog .dispatch-load__stop-summary").TextContent
    );
    Assert.Equal(
      "Stops in route order",
      dialog.Find("dialog .dispatch-paper__stops").GetAttribute("aria-label")
    );
  }

  [Fact]
  public void RefreshExpandsTheSameCardAndRemovesObsoleteRepeatLabelsWithoutHidingCompletedStops()
  {
    using var context = Context();
    var load = Load();
    var expanded = load.Stops;
    load.Stops = [expanded[0], expanded[^1]];
    var card = context.Render<DispatchLoadCard>(parameters =>
      parameters.Add(component => component.Load, load)
    );
    Assert.Empty(
      card.FindAll(".dispatch-load__stop-summary, .dispatch-load__stop-visit")
    );
    Assert.Equal(2, card.FindAll(".dispatch-load__stop").Count);

    load.Stops = expanded;
    card.Render(parameters =>
      parameters.Add(component => component.Load, load)
    );
    AssertStops(card.Find(".dispatch-load"), expanded);

    load.Stops = [expanded[0], expanded[1], expanded[^1]];
    card.Render(parameters =>
      parameters.Add(component => component.Load, load)
    );
    Assert.Empty(card.FindAll(".dispatch-load__stop-visit"));
    Assert.Equal(
      "3 stops · 2 pickups · 1 delivery",
      card.Find(".dispatch-load__stop-summary").TextContent
    );
    Assert.Single(card.FindAll(".dispatch-load__stop--completed"));
  }

  [Fact]
  public void TableSummarizesGroupsAndLinksEveryVisitWithoutExpandingTheRow()
  {
    using var context = Context();
    var load = Load();
    load.Stops[1].Job = "Drop Off";
    var table = context.Render<DispatchTable>(parameters =>
      parameters.Add(
        component => component.Trucks,
        [new TruckDispatchBoardResponse { Key = "truck", Dispatches = [load] }]
      )
    );

    Assert.Equal(
      new[] { "Stop 3 · Visit 2 of 3" },
      table
        .FindAll(
          ".dispatch-table__stop.is-pickup .dispatch-table__stop-position"
        )
        .Select(position => position.TextContent)
    );
    Assert.Equal(
      new[] { "Stop 2" },
      table
        .FindAll(
          ".dispatch-table__stop.is-delivery .dispatch-table__stop-position"
        )
        .Select(position => position.TextContent)
    );
    Assert.Equal(
      2,
      table.FindAll(".dispatch-table__stop-entry[data-stop-id]").Count
    );
    Assert.Equal(
      new[] { "3 pickups · 1 completed", "2 deliveries · 0 completed" },
      table
        .FindAll(".dispatch-table__stops-summary")
        .Select(button => button.TextContent)
    );
    Assert.Empty(table.FindAll(".dispatch-table__stop-completed"));
    Assert.Equal(8, table.FindAll("tbody tr:first-child td").Count);
    Assert.Equal(
      $"/dispatch/{load.Id}",
      table.Find(".dispatch-table__stops-summary").GetAttribute("href")
    );
    Assert.Empty(table.FindAll("dialog"));
    var dialog = context.Render<DispatchLoadDialog>(p =>
      p.Add(x => x.Load, load)
    );
    AssertStops(dialog.Find("dialog"), load.Stops);
    Assert.Equal(
      2,
      table.FindAll(".dispatch-table__stop-entry[data-stop-id]").Count
    );

    load.Stops = [load.Stops[0], load.Stops[^1]];
    table.Render();
    Assert.Empty(table.FindAll(".dispatch-table__stop-position"));
    Assert.Empty(table.FindAll(".dispatch-table__stops-summary"));
  }

  private static void AssertStops(
    IElement parent,
    IReadOnlyList<DispatchStopResponse> expected
  )
  {
    var stops = parent.QuerySelectorAll(".dispatch-load__stop");
    Assert.Equal(
      expected.Select(stop => stop.Id.ToString()),
      stops.Select(stop => stop.GetAttribute("data-stop-id"))
    );
    Assert.Equal(
      new[] { "1", "2", "3", "4", "5" },
      stops.Select(stop =>
        stop.QuerySelector(".dispatch-load__stop-number")!.TextContent
      )
    );
    Assert.Equal(
      parent.QuerySelector(".dispatch-load__history-stop") is null
        ? new[] { "Visit 1 of 3", "Visit 2 of 3", "Visit 3 of 3" }
        : new[] { "Visit 2 of 3", "Visit 3 of 3" },
      parent
        .QuerySelectorAll(".dispatch-load__stop-visit")
        .Select(visit => visit.TextContent)
    );
    var times = new[]
    {
      "02:00 AM",
      "11:00 AM",
      "01:00 PM",
      "02:00 PM",
      "05:00 AM",
    };
    for (var index = 0; index < stops.Length; index++)
      Assert.Contains(
        times[index],
        stops[index]
          .QuerySelector(
            ".arrival-estimate__appointment, .dispatch-load__history-time"
          )!
          .TextContent
      );
    Assert.Single(parent.QuerySelectorAll(".dispatch-load__stop--completed"));
    foreach (var visit in parent.QuerySelectorAll(".dispatch-load__stop-visit"))
    {
      Assert.Equal(
        $"{visit.TextContent} at this address",
        visit.GetAttribute("title")
      );
      Assert.Equal(
        "FAIRLIFE WEBSTER",
        visit
          .Closest(".dispatch-load__stop")!
          .QuerySelector(".dispatch-load__location")!
          .GetAttribute("title")
      );
    }
  }

  private static BunitContext Context()
  {
    var context = new BunitContext();
    context.Services.AddSingleton(TimeProvider.System);
    context.JSInterop.SetupModule("./js/generated/shared/loadDialog.js").Mode =
      JSRuntimeMode.Loose;
    return context;
  }

  private static DispatchResponse Load()
  {
    var stops = Enumerable
      .Range(1, 5)
      .Select(sequence => new DispatchStopResponse
      {
        Id = Guid.NewGuid(),
        Sequence = sequence,
        Job = sequence == 5 ? "Drop Off" : "Pick Up",
        Name = "FAIRLIFE WEBSTER",
        Address = "1886 Tebor Rd",
        City = "Webster",
        Province = "NY",
        Country = "US",
        ScheduledDate = new(2026, 9, sequence == 5 ? 14 : 11),
        ScheduledTime = new(new[] { 2, 11, 13, 14, 5 }[sequence - 1], 0),
      })
      .ToList();
    stops[0].PickedUpAt = new(2026, 9, 11, 2, 15, 0);
    stops[1].Name = "Target DC #3802";
    stops[1].Address = "1730 NY-5S";
    stops[1].City = "Amsterdam";
    stops[4].Name = "COSTCO SE DEPOT 174";
    stops[4].Address = "13077 SW Anthony F. Sansone Sr. Blvd";
    stops[4].City = "Port Saint Lucie";
    stops[4].Province = "FL";
    return new()
    {
      Id = Guid.NewGuid(),
      LoadNumber = 1383,
      Status = "in_transit",
      Stops = stops,
    };
  }
}
