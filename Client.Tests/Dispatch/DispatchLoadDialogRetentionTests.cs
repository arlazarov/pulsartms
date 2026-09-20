using Bunit;
using Client.Models.DTO.Dispatch;
using Client.Models.DTO.Planning;
using Client.Pages.Dispatch;
using Client.Shared;
using Client.Shared.Dispatch;
using Client.Shared.Dispatch.DispatchLoadDialog;
using Client.Shared.Dispatch.DispatchLoadStop;
using Client.Tests.Support;
using Microsoft.AspNetCore.Components.Web;

namespace Client.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Component")]
public sealed class DispatchLoadDialogRetentionTests
{
  [Theory]
  [InlineData(0)]
  [InlineData(1)]
  [InlineData(2)]
  public async Task DueBoardRefreshRetainsForecastBeforeItsTelemetryRequestCompletes(
    int view
  )
  {
    await using var fixture = new DispatchRefreshFixture();
    fixture
      .Context.JSInterop.SetupModule("./js/generated/shared/loadDialog.js")
      .Mode = JSRuntimeMode.Loose;
    fixture.Load.Eta = WithCycles(fixture.Forecast(), false);
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Single(component.FindComponents<DispatchLoadCard>())
    );
    await fixture.InitialTelemetryRequested.WaitAsync(TimeSpan.FromSeconds(5));
    await component.InvokeAsync(
      () =>
        component
          .FindAll(".dispatch-view button")[view]
          .ClickAsync(new MouseEventArgs())
    );
    var opener = view switch
    {
      1 => ".dispatch-table__open",
      2 => ".dispatch-paper__tab",
      _ => ".dispatch-load__details",
    };
    Assert.Equal(
      $"/dispatch/{fixture.Load.Id}",
      component.Find(opener).GetAttribute("href")
    );
    Assert.Empty(component.FindAll("dialog"));
    var dialog = fixture.Context.Render<DispatchLoadDialog>(p =>
      p.Add(x => x.Load, ViewState(component, view).Load)
    );
    var originalDialog = dialog.Instance;
    var original = Snapshot(dialog);
    Assert.Equal(2, dialog.FindAll(".stop-hours__arrival-cycle").Count);
    await component.InvokeAsync(
      () => fixture.Context.Visibility.SetVisible(false)
    );
    await component.InvokeAsync(
      () => fixture.Clock.Advance(TimeSpan.FromSeconds(61))
    );
    fixture.DeferTelemetry = true;
    await component.InvokeAsync(
      () => fixture.Context.Visibility.SetVisible(true)
    );
    await fixture.NextTelemetryRequest();
    component.WaitForAssertion(() =>
    {
      Assert.True(ViewState(component, view).Refreshing);
      SyncDialog(component, view, dialog);
      Assert.True(dialog.Instance.Refreshing);
    });
    await component.InvokeAsync(
      () => fixture.Clock.Advance(TimeSpan.FromMinutes(2))
    );
    SyncDialog(component, view, dialog);
    Assert.Equal(original, Snapshot(dialog));
    Assert.Equal(2, dialog.FindAll(".stop-hours__arrival-cycle").Count);
    await component.InvokeAsync(
      () => fixture.Clock.Advance(TimeSpan.FromMinutes(15))
    );
    SyncDialog(component, view, dialog);
    Assert.Empty(
      dialog.FindAll(".stop-hours__arrival-cycle, .stop-hours__road")
    );
  }

  [Theory]
  [InlineData(0, false)]
  [InlineData(1, false)]
  [InlineData(2, false)]
  [InlineData(0, true)]
  [InlineData(1, true)]
  [InlineData(2, true)]
  public async Task EveryViewKeepsAnOpenDialogsForecastDuringHeldRefreshAndReplacesOrClearsIt(
    int view,
    bool legacyCycle
  )
  {
    await using var fixture = new DispatchRefreshFixture();
    fixture
      .Context.JSInterop.SetupModule("./js/generated/shared/loadDialog.js")
      .Mode = JSRuntimeMode.Loose;
    fixture.Load.Eta = WithCycles(fixture.Forecast(), legacyCycle);
    var component = fixture.Render();
    component.WaitForAssertion(
      () => Assert.Single(component.FindComponents<DispatchLoadCard>())
    );
    if (view != 0)
      await component.InvokeAsync(
        () =>
          component
            .FindAll(".dispatch-view button")[view]
            .ClickAsync(new MouseEventArgs())
      );
    var opener = view switch
    {
      1 => ".dispatch-table__open",
      2 => ".dispatch-paper__tab",
      _ => ".dispatch-load__details",
    };
    Assert.Equal(
      $"/dispatch/{fixture.Load.Id}",
      component.Find(opener).GetAttribute("href")
    );
    Assert.Empty(component.FindAll("dialog"));
    var dialog = fixture.Context.Render<DispatchLoadDialog>(p =>
      p.Add(x => x.Load, ViewState(component, view).Load)
    );
    var originalDialog = dialog.Instance;
    Assert.Equal(
      2,
      dialog.FindAll(".dispatch-load__stop-times .arrival-estimate").Count
    );
    Assert.Equal(
      legacyCycle ? 2 : 0,
      dialog.FindAll(".dispatch-load__stop-cycle").Count
    );
    Assert.Equal(
      legacyCycle ? 0 : 2,
      dialog.FindAll(".stop-hours__arrival-cycle").Count
    );
    var original = Snapshot(dialog);

    fixture.DeferBoard = true;
    await component.InvokeAsync(
      () => fixture.Clock.Advance(TimeSpan.FromSeconds(61))
    );
    var held = await fixture.NextRequest();
    component.WaitForAssertion(() =>
    {
      Assert.True(ViewState(component, view).Refreshing);
      SyncDialog(component, view, dialog);
      Assert.True(dialog.Instance.Refreshing);
    });
    await component.InvokeAsync(
      () => fixture.Clock.Advance(TimeSpan.FromMinutes(2))
    );
    SyncDialog(component, view, dialog);
    Assert.Equal(original, Snapshot(dialog));

    fixture.Load.Eta = WithCycles(fixture.Forecast(5), legacyCycle, 300);
    fixture.DeferBoard = false;
    held.SetResult(fixture.Board());
    component.WaitForAssertion(() =>
    {
      Assert.False(ViewState(component, view).Refreshing);
      SyncDialog(component, view, dialog);
      Assert.False(dialog.Instance.Refreshing);
      // One selector for both: a stop with no hours behind it is drawn by
      // the same forecast component, so lateness is marked the same way.
      Assert.Equal(
        2,
        dialog
          .FindAll(
            ".stop-hours__road .stop-hours__arrival .stop-hours__status--danger"
          )
          .Count
      );
      Assert.Contains(legacyCycle ? "~5h 00m" : "+5h 00m", dialog.Markup);
      Assert.NotEqual(original, Snapshot(dialog));
    });
    Assert.Same(originalDialog, dialog.Instance);

    fixture.DeferBoard = true;
    await component.InvokeAsync(
      () => fixture.Clock.Advance(TimeSpan.FromSeconds(61))
    );
    var invalidated = await fixture.NextRequest();
    fixture.Load.Eta = fixture.Forecast() with
    {
      Stops = [],
      RouteUpdatePending = false,
      UnavailableReason = "Position unavailable",
    };
    fixture.DeferBoard = false;
    invalidated.SetResult(fixture.Board());
    component.WaitForAssertion(() =>
    {
      Assert.False(ViewState(component, view).Refreshing);
      SyncDialog(component, view, dialog);
      Assert.False(dialog.Instance.Refreshing);
      Assert.Empty(dialog.Instance.Load.Eta!.Stops);
      Assert.False(dialog.Instance.Load.Eta.RouteUpdatePending);
      Assert.All(
        dialog.FindComponents<DispatchLoadStop>(),
        stop =>
        {
          Assert.False(stop.Instance.Refreshing);
          Assert.Empty(stop.Instance.Eta!.Stops);
          Assert.False(stop.Instance.Eta.RouteUpdatePending);
        }
      );
      Assert.Empty(
        dialog.FindAll(
          ".dispatch-load-dialog .stop-hours__road, .dispatch-load-dialog .stop-hours__arrival-cycle, .dispatch-load-dialog .dispatch-load__stop-cycle, .dispatch-load-dialog .dispatch-load__cycle"
        )
      );
      Assert.Equal(
        2,
        dialog
          .FindAll(".dispatch-load-dialog .dispatch-load__eta-missing")
          .Count
      );
    });
  }

  [Fact]
  public async Task RefreshingDialogDoesNotExtendTheExistingGraceOrRetainCompletedStops()
  {
    await using var fixture = new DispatchRefreshFixture();
    fixture
      .Context.JSInterop.SetupModule("./js/generated/shared/loadDialog.js")
      .Mode = JSRuntimeMode.Loose;
    fixture.Load.Eta = WithCycles(fixture.Forecast(), false);
    var dialog = fixture.Context.Render<DispatchLoadDialog>(p =>
      p.Add(view => view.Load, fixture.Load).Add(view => view.Refreshing, true)
    );
    Assert.Equal(2, dialog.FindAll(".stop-hours__arrival-cycle").Count);
    fixture.Clock.Advance(TimeSpan.FromMinutes(18));
    dialog.Render();
    Assert.Empty(
      dialog.FindAll(".stop-hours__arrival-cycle, .stop-hours__road")
    );

    fixture.Load.Eta = WithCycles(fixture.Forecast(), false);
    dialog.Render();
    Assert.Equal(2, dialog.FindAll(".stop-hours__arrival-cycle").Count);
    fixture.Load.Status = "completed";
    dialog.Render();
    Assert.Equal(2, dialog.FindAll(".dispatch-load__stop--completed").Count);
    Assert.Empty(
      dialog.FindAll(
        ".stop-hours__arrival-cycle, .stop-hours__road, .dispatch-load__eta-missing"
      )
    );
  }

  private static (DispatchResponse Load, bool Refreshing) ViewState(
    IRenderedComponent<DispatchList> component,
    int view
  )
  {
    if (view == 0)
    {
      var card = component.FindComponent<DispatchLoadCard>().Instance;
      return (card.Load, card.Refreshing);
    }
    if (view == 1)
    {
      var table = component.FindComponent<DispatchTable>().Instance;
      return (
        table.Trucks.SelectMany(x => x.Dispatches).Single(),
        table.Refreshing
      );
    }
    var papers = component.FindComponent<DispatchPapers>().Instance;
    return (
      papers.Trucks.SelectMany(x => x.Dispatches).Single(),
      papers.Refreshing
    );
  }

  private static void SyncDialog(
    IRenderedComponent<DispatchList> component,
    int view,
    IRenderedComponent<DispatchLoadDialog> dialog
  )
  {
    var state = ViewState(component, view);
    dialog.Render(p =>
      p.Add(x => x.Load, state.Load).Add(x => x.Refreshing, state.Refreshing)
    );
  }

  private static string[] Snapshot(
    IRenderedComponent<DispatchLoadDialog> dialog
  ) =>
    dialog
      .FindAll(
        ".arrival-estimate, .dispatch-load__stop-cycle, .dispatch-load__cycle"
      )
      .Select(node => node.TextContent)
      .ToArray();

  private static DispatchEta WithCycles(
    DispatchEta eta,
    bool legacy,
    int minutes = 600
  ) =>
    eta with
    {
      Stops = eta
        .Stops.Select(stop =>
          stop with
          {
            Hours = legacy
              ? null
              : new StopHoursForecast(
                minutes,
                minutes,
                0,
                null,
                true,
                [],
                null
              ),
            CycleAfterDeparture = legacy
              ? new StopCycleForecast(minutes, null, null, null, false)
              : null,
          }
        )
        .ToArray(),
    };
}
