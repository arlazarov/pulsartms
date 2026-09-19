using Bunit;
using Client.Models.DTO.Dispatch;
using Client.Models.DTO.Planning;
using Client.Pages.Dispatch;
using Client.Shared.Dispatch;
using Client.Shared.Dispatch.DispatchLoadDialog;
using Client.Shared.Dispatch.DispatchLoadStop;
using Client.Tests.Support;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Client.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Component")]
public sealed class DispatchStopCycleTests
{
  private static readonly DateTimeOffset Start = new(
    2026,
    9,
    8,
    12,
    0,
    0,
    TimeSpan.Zero
  );

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task EachStopOwnsASeparateArrivalCycleSectionWithoutDepartureBalance(
    bool current
  )
  {
    await using var context = new ClientComponentContext(
      (_, _) =>
        throw new InvalidOperationException(
          "Cycle sections must use the board forecast."
        )
    );
    context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Start));
    var load = Load();
    load.Eta = load.Eta! with
    {
      Stops = load
        .Eta.Stops.Select(
          (stop, index) =>
            stop with
            {
              Hours = new StopHoursForecast(
                index == 0 ? 600 : 300,
                index == 0 ? 480 : -120,
                null,
                null,
                true,
                [],
                null
              ),
            }
        )
        .Reverse()
        .ToArray(),
    };
    var component = context.Render<DispatchLoadCard>(p =>
      p.Add(x => x.Load, load).Add(x => x.Current, current)
    );
    Assert.Empty(component.FindAll("section.stop-hours__cycle, details"));
    var dialog = RenderDetails(context, component);
    var stops = dialog.FindAll(".dispatch-load-dialog .dispatch-load__stop");
    Assert.Equal(2, stops.Count);
    foreach (var stop in stops)
    {
      var cycle = Assert.Single(
        stop.QuerySelectorAll("section.stop-hours__cycle")
      );
      Assert.Equal("Cycle remaining", cycle.QuerySelector("h3")!.TextContent);
      Assert.Null(cycle.QuerySelector(".stop-hours__road"));
    }
    Assert.Equal(
      "+10h 00m",
      stops[0].QuerySelector(".stop-hours__arrival-cycle strong")!.TextContent
    );
    Assert.Null(stops[0].QuerySelector(".stop-hours__departure-cycle"));
    Assert.Equal(
      "+5h 00m",
      stops[1].QuerySelector(".stop-hours__arrival-cycle strong")!.TextContent
    );
    Assert.Null(stops[1].QuerySelector(".stop-hours__departure-cycle"));
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task CurrentAndFutureCardsShowEachStopsOwnCycleWithoutRequests(
    bool current
  )
  {
    var requests = 0;
    await using var context = new ClientComponentContext(
      (_, _) =>
      {
        requests++;
        throw new InvalidOperationException(
          "Stop cycle presentation must use the board forecast."
        );
      }
    );
    context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Start));
    var load = Load();
    load.Eta = load.Eta! with { Stops = load.Eta.Stops.Reverse().ToArray() };
    var component = context.Render<DispatchLoadCard>(parameters =>
      parameters.Add(card => card.Load, load).Add(card => card.Current, current)
    );

    Assert.Empty(component.FindAll(".dispatch-load__stop-cycle, details"));
    var dialog = RenderDetails(context, component);
    var rows = dialog.FindAll(
      ".dispatch-load-dialog .dispatch-load__stop-cycle"
    );
    Assert.Equal(2, rows.Count);
    Assert.All(
      rows,
      row => Assert.Contains("Cycle after stop", row.TextContent)
    );
    Assert.Equal("~18h 20m", rows[0].QuerySelector("strong")!.TextContent);
    Assert.Equal("~12h 30m", rows[1].QuerySelector("strong")!.TextContent);
    Assert.All(rows, row => Assert.Empty(row.QuerySelectorAll("small")));
    Assert.Equal(0, requests);
  }

  [Theory]
  [InlineData("dispatch")]
  [InlineData("stop")]
  [InlineData("empty-dispatch")]
  [InlineData("empty-stop")]
  public void WrongOrEmptyIdentityCannotShowAnotherStopsCycle(string mismatch)
  {
    using var context = Context();
    var load = Load();
    if (mismatch == "dispatch")
      load.Id = Guid.NewGuid();
    if (mismatch == "stop")
      load.Stops[0].Id = Guid.NewGuid();
    if (mismatch == "empty-dispatch")
    {
      load.Id = Guid.Empty;
      load.Eta = load.Eta! with
      {
        Stops = load
          .Eta.Stops.Select(stop => stop with { DispatchId = Guid.Empty })
          .ToArray(),
      };
    }
    if (mismatch == "empty-stop")
    {
      load.Stops[0].Id = Guid.Empty;
      load.Eta = load.Eta! with
      {
        Stops =
        [
          load.Eta.Stops[0] with
          {
            StopId = Guid.Empty,
          },
          load.Eta.Stops[1],
        ],
      };
    }
    var component = Render(context, load);

    Assert.Empty(component.FindAll(".dispatch-load__stop-cycle"));
  }

  [Fact]
  public void MatchingStopIsSelectedByBothIdsEvenWhenWrongDispatchAppearsFirst()
  {
    using var context = Context();
    var load = Load();
    load.Eta = load.Eta! with
    {
      Stops =
      [
        load.Eta.Stops[0] with
        {
          DispatchId = Guid.NewGuid(),
          CycleAfterDeparture = Cycle(5),
        },
        .. load.Eta.Stops,
      ],
    };

    Assert.Equal("~18h 20m", Value(Render(context, load)));
  }

  [Theory]
  [InlineData(null, null)]
  [InlineData(-1, null)]
  [InlineData(0, "~0h 00m")]
  public void MissingInvalidAndExhaustedCycleAreDistinct(
    int? remaining,
    string? expected
  )
  {
    using var context = Context();
    var load = Load();
    load.Eta = load.Eta! with
    {
      Stops =
      [
        load.Eta.Stops[0] with
        {
          CycleAfterDeparture = remaining is { } minutes
            ? Cycle(minutes)
            : null,
        },
      ],
    };

    var component = Render(context, load);
    if (expected is null)
      Assert.Empty(component.FindAll(".dispatch-load__stop-cycle"));
    else
      Assert.Equal(expected, Value(component));
  }

  [Fact]
  public void ExpiredForecastWithoutPendingUpdateIsNotDisplayed()
  {
    var clock = new FakeTimeProvider(Start);
    using var context = Context(clock);
    var load = Load();
    var component = Render(context, load);
    clock.Advance(TimeSpan.FromMinutes(3));
    Update(component, load);

    Assert.Empty(component.FindAll(".dispatch-load__stop-cycle"));
    Assert.Empty(component.FindAll(".dispatch-load__stop-cycle small"));
  }

  [Fact]
  public void PendingUpdateRetainsCycleOnlyWithinTheExistingDisplayGrace()
  {
    var clock = new FakeTimeProvider(Start);
    using var context = Context(clock);
    var load = Load();
    var component = Render(context, load);
    var previousMarkup = component.Markup;
    clock.Advance(TimeSpan.FromMinutes(3));
    load.Eta = load.Eta! with { Stops = [], RouteUpdatePending = true };
    Update(component, load);

    Assert.Equal("~18h 20m", Value(component));
    Assert.Equal(previousMarkup, component.Markup);
    Assert.Empty(component.FindAll(".dispatch-load__stop-cycle small"));

    clock.Advance(TimeSpan.FromMinutes(15));
    Update(component, load);
    Assert.Empty(component.FindAll(".dispatch-load__stop-cycle"));
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public void ChangedStopOrDispatchCannotInheritPendingCycle(
    bool changeDispatch
  )
  {
    using var context = Context();
    var load = Load();
    var component = Render(context, load);
    if (changeDispatch)
      load.Id = Guid.NewGuid();
    else
      load.Stops[0].Id = Guid.NewGuid();
    load.Eta = load.Eta! with { Stops = [], RouteUpdatePending = true };
    Update(component, load);

    Assert.Empty(component.FindAll(".dispatch-load__stop-cycle"));
  }

  [Fact]
  public void CompletedStopHidesCycleAndClearsItsRetainedSnapshot()
  {
    using var context = Context();
    var load = Load();
    var component = Render(context, load);
    load.Stops[0].PickedUpAt = Start.UtcDateTime;
    load.Eta = load.Eta! with { Stops = [], RouteUpdatePending = true };
    Update(component, load);
    Assert.Empty(component.FindAll(".dispatch-load__stop-cycle"));

    load.Stops[0].PickedUpAt = null;
    Update(component, load);
    Assert.Empty(component.FindAll(".dispatch-load__stop-cycle"));
  }

  [Fact]
  public void PendingMatchingForecastKeepsTheCompleteCycleUntilAReadySnapshotReplacesIt()
  {
    using var context = Context();
    var load = Load();
    var component = Render(context, load);
    var previousMarkup = component.Markup;
    load.Eta = load.Eta! with
    {
      CalculatedAt = Start.AddMinutes(1).UtcDateTime,
      Stops = [load.Eta.Stops[0] with { CycleAfterDeparture = null }],
      RouteUpdatePending = true,
    };
    Update(component, load);

    Assert.Equal("~18h 20m", Value(component));
    Assert.Equal(previousMarkup, component.Markup);
    Assert.DoesNotContain("Updating", component.Markup);

    load.Eta = load.Eta with { RouteUpdatePending = false };
    Update(component, load);
    Assert.Empty(component.FindAll(".dispatch-load__stop-cycle"));
    Assert.DoesNotContain("Updating", component.Markup);
  }

  private static IRenderedComponent<DispatchLoadDialog> RenderDetails(
    BunitContext context,
    IRenderedComponent<DispatchLoadCard> component
  )
  {
    context.JSInterop.SetupModule("./js/generated/shared/loadDialog.js").Mode =
      JSRuntimeMode.Loose;
    Assert.Equal(
      $"/dispatch/{component.Instance.Load.Id}",
      component.Find(".dispatch-load__details").GetAttribute("href")
    );
    Assert.Empty(component.FindAll("dialog"));
    return context.Render<DispatchLoadDialog>(p =>
      p.Add(x => x.Load, component.Instance.Load)
    );
  }

  private static string Value(IRenderedComponent<DispatchLoadStop> component) =>
    component.Find(".dispatch-load__stop-cycle strong").TextContent;

  private static IRenderedComponent<DispatchLoadStop> Render(
    BunitContext context,
    DispatchResponse load
  ) =>
    context.Render<DispatchLoadStop>(parameters =>
      parameters
        .Add(stop => stop.DispatchId, load.Id)
        .Add(stop => stop.Stop, load.Stops[0])
        .Add(stop => stop.Number, 1)
        .Add(stop => stop.Eta, load.Eta)
    );

  private static void Update(
    IRenderedComponent<DispatchLoadStop> component,
    DispatchResponse load
  ) =>
    component.Render(parameters =>
      parameters
        .Add(stop => stop.DispatchId, load.Id)
        .Add(stop => stop.Stop, load.Stops[0])
        .Add(stop => stop.Number, 1)
        .Add(stop => stop.Eta, load.Eta)
    );

  private static BunitContext Context(FakeTimeProvider? clock = null)
  {
    var context = new BunitContext();
    context.Services.AddSingleton<TimeProvider>(
      clock ?? new FakeTimeProvider(Start)
    );
    return context;
  }

  private static StopCycleForecast Cycle(int remaining) =>
    new(remaining, null, null, null, false);

  private static DispatchResponse Load()
  {
    var load = new DispatchResponse
    {
      Id = Guid.NewGuid(),
      LoadNumber = 1000,
      Stops = Enumerable
        .Range(1, 2)
        .Select(index => new DispatchStopResponse
        {
          Id = Guid.NewGuid(),
          Sequence = index,
          Job = index == 1 ? "Pickup" : "Delivery",
          Name = $"Facility {index}",
          City = "City",
          Province = "CA",
        })
        .ToList(),
    };
    load.Eta = new(
      Start.UtcDateTime,
      Start.AddMinutes(2).UtcDateTime,
      load.Stops.Select(
          (stop, index) =>
            new StopEta(
              stop.Id,
              Start.AddHours(index * 2),
              "America/Los_Angeles",
              null,
              0,
              60,
              0
            )
            {
              DispatchId = load.Id,
              Departure = Start.AddHours(index * 2 + 2),
              CycleAfterDeparture = Cycle(index == 0 ? 1100 : 750),
            }
        )
        .ToArray(),
      null,
      []
    );
    return load;
  }
}
