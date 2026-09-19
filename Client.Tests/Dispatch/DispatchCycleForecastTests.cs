using Bunit;
using Client.Models.DTO.Dispatch;
using Client.Models.DTO.Planning;
using Client.Pages.Dispatch;
using Client.Shared.Dispatch;
using Client.Shared.Dispatch.DispatchCycleForecast;
using Client.Shared.Dispatch.DispatchLoadDialog;
using Client.Tests.Support;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Client.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Component")]
public sealed class DispatchCycleForecastTests
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

  [Fact]
  public async Task PerStopHoursKeepPickupDeliveryCyclesInTheDialogWithoutRepeatingTheTruckRecap()
  {
    await using var context = Context();
    var load = Load();
    load.Eta = load.Eta! with
    {
      Stops = load
        .Eta.Stops.Select(stop =>
          stop with
          {
            Hours = new(870, 750, 0, null, true, [], null),
          }
        )
        .ToArray(),
    };
    var component = context.Render<DispatchLoadCard>(p =>
      p.Add(x => x.Load, load)
    );
    Assert.Empty(
      component.FindAll(
        ".stop-hours__arrival-cycle, .dispatch-load__more-details"
      )
    );
    var dialog = RenderDetails(context, component);
    Assert.Equal(
      2,
      dialog.FindAll(".dispatch-load-dialog .stop-hours__arrival-cycle").Count
    );
    Assert.Empty(dialog.FindAll(".stop-hours__departure-cycle"));
    Assert.Empty(dialog.FindAll(".dispatch-load__cycle"));
    Assert.Equal(
      2,
      dialog.FindAll(".dispatch-load-dialog .dispatch-load__more-details").Count
    );
    Assert.DoesNotContain("Next recap", dialog.Markup);
    Assert.DoesNotContain("+3h 05m", dialog.Markup);
  }

  [Fact]
  public async Task FutureCardKeepsLegacyFinalDepartureCycleInTheDialogWithoutRepeatingRecapOrMakingRequests()
  {
    var requests = 0;
    await using var context = new ClientComponentContext(
      (_, _) =>
      {
        requests++;
        throw new InvalidOperationException(
          "Cycle presentation must use the board forecast."
        );
      }
    );
    context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Start));
    var load = Load();
    load.Stops.Reverse();
    load.Eta = load.Eta! with { Stops = load.Eta.Stops.Reverse().ToArray() };

    var component = context.Render<DispatchLoadCard>(parameters =>
      parameters.Add(card => card.Load, load)
    );
    Assert.Empty(component.FindAll(".dispatch-load__cycle, details"));
    var dialog = RenderDetails(context, component);
    var summary = dialog.Find(".dispatch-load-dialog .dispatch-load__cycle");
    Assert.Equal(
      2,
      dialog.FindAll(".dispatch-load-dialog .dispatch-load__more-details").Count
    );

    Assert.Contains("Cycle after delivery", summary.TextContent);
    Assert.Contains("~12h 30m", summary.TextContent);
    Assert.DoesNotContain("~1h 00m", summary.TextContent);
    Assert.Empty(summary.QuerySelectorAll("time"));
    Assert.DoesNotContain("Next recap", summary.TextContent);
    Assert.DoesNotContain("+3h 05m", summary.TextContent);
    Assert.DoesNotContain("Sep 13", summary.TextContent);
    Assert.DoesNotContain("+13h 18m", summary.TextContent);
    Assert.DoesNotContain("home", summary.TextContent);
    Assert.DoesNotContain("UTC", summary.TextContent);
    Assert.Empty(summary.QuerySelectorAll("small"));
    Assert.Single(summary.QuerySelectorAll("dd"));
    Assert.Equal(0, requests);
  }

  [Fact]
  public void CurrentCardDoesNotShowTheFutureLoadSummary()
  {
    using var context = Context();
    var component = context.Render<DispatchLoadCard>(parameters =>
      parameters.Add(card => card.Load, Load()).Add(card => card.Current, true)
    );

    Assert.Empty(component.FindAll(".dispatch-load__cycle"));
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

  [Fact]
  public void MatchingFinalStopMustBelongToTheExactDispatch()
  {
    using var context = Context();
    var load = Load();
    var final = load.Eta!.Stops[^1];
    var wrongDispatch = final with
    {
      DispatchId = Guid.NewGuid(),
      CycleAfterDeparture = Cycle(999),
    };
    load.Eta = load.Eta with { Stops = [wrongDispatch, .. load.Eta.Stops] };
    var component = context.Render<DispatchCycleForecast>(parameters =>
      parameters.Add(card => card.Load, load)
    );

    Assert.Contains("~12h 30m", component.Markup);
    Assert.DoesNotContain("~16h 39m", component.Markup);

    load.Eta = load.Eta with { Stops = [wrongDispatch, load.Eta.Stops[1]] };
    component.Render(parameters => parameters.Add(card => card.Load, load));
    Assert.Empty(component.FindAll(".dispatch-load__cycle"));
    Assert.Empty(component.FindAll("time"));
  }

  [Fact]
  public void MissingFinalStopForecastDoesNotBorrowEarlierOrLaterArrival()
  {
    using var context = Context();
    var load = Load();
    load.Eta = load.Eta! with
    {
      Stops = [load.Eta.Stops[0] with { Arrival = Start.AddDays(50) }],
    };
    var component = context.Render<DispatchCycleForecast>(parameters =>
      parameters.Add(card => card.Load, load)
    );

    Assert.Empty(component.FindAll(".dispatch-load__cycle"));
    Assert.Empty(component.FindAll("time"));
  }

  [Fact]
  public void UnverifiedRecapDoesNotPresentAnUntrustedTimeOrAmount()
  {
    using var context = Context();
    var load = Load();
    SetBaselineCycle(load, BaselineCycle() with { RecapVerified = false });
    var component = context.Render<DispatchCycleForecast>(parameters =>
      parameters.Add(card => card.Load, load)
    );

    Assert.Contains("~12h 30m", component.Markup);
    Assert.DoesNotContain("Unknown", component.Markup);
    Assert.DoesNotContain("Next recap", component.Markup);
    Assert.Empty(component.FindAll("time"));
    Assert.DoesNotContain("+3h 05m", component.Markup);
    Assert.DoesNotContain("+13h 18m", component.Markup);
  }

  [Fact]
  public void MissingCalculationSnapshotDoesNotBorrowRecapAfterDelivery()
  {
    using var context = Context();
    var load = Load();
    SetBaselineCycle(load, null);
    var component = context.Render<DispatchCycleForecast>(parameters =>
      parameters.Add(card => card.Load, load)
    );

    Assert.Contains("~12h 30m", component.Markup);
    Assert.Single(component.FindAll(".dispatch-load__cycle dd"));
    Assert.DoesNotContain("Next recap", component.Markup);
    Assert.Empty(component.FindAll("time"));
    Assert.DoesNotContain("+13h 18m", component.Markup);
  }

  [Fact]
  public void VerifiedHorizonWithoutPositiveRecapIsDistinctFromUnknown()
  {
    using var context = Context();
    var load = Load();
    SetBaselineCycle(
      load,
      BaselineCycle() with
      {
        NextRecapAt = null,
        NextRecapMinutes = null,
      }
    );
    var component = context.Render<DispatchCycleForecast>(parameters =>
      parameters.Add(card => card.Load, load)
    );

    Assert.DoesNotContain("No upcoming recap", component.Markup);
    Assert.DoesNotContain("Next recap", component.Markup);
    Assert.DoesNotContain("Unknown", component.Markup);
    Assert.Empty(component.FindAll("time"));
  }

  [Fact]
  public void ExpiredForecastWithoutPendingUpdateIsUnavailable()
  {
    var clock = new FakeTimeProvider(Start);
    using var context = Context(clock);
    var load = Load();
    var component = context.Render<DispatchCycleForecast>(parameters =>
      parameters.Add(card => card.Load, load)
    );
    clock.Advance(TimeSpan.FromMinutes(3));
    component.Render(parameters => parameters.Add(card => card.Load, load));

    Assert.Empty(component.FindAll(".dispatch-load__cycle"));
    Assert.Empty(component.FindAll("time"));
    Assert.Empty(component.FindAll(".dispatch-load__cycle-updating"));
  }

  [Fact]
  public void PendingUpdateRetainsTheSameForecastOnlyWithinExistingDisplayGrace()
  {
    var clock = new FakeTimeProvider(Start);
    using var context = Context(clock);
    var load = Load();
    var complete = load.Eta!;
    var component = context.Render<DispatchCycleForecast>(parameters =>
      parameters.Add(card => card.Load, load)
    );
    var previousMarkup = component.Markup;
    clock.Advance(TimeSpan.FromMinutes(3));
    load.Eta = load.Eta! with
    {
      Stops = [],
      RouteUpdatePending = true,
      CycleAtCalculation = null,
    };
    component.Render(parameters => parameters.Add(card => card.Load, load));

    Assert.Contains("~12h 30m", component.Markup);
    Assert.Empty(component.FindAll("time"));
    Assert.Equal(previousMarkup, component.Markup);
    Assert.Empty(component.FindAll(".dispatch-load__cycle-updating"));
    Assert.DoesNotContain("Updating", component.Markup);

    clock.Advance(TimeSpan.FromMinutes(15));
    component.Render(parameters => parameters.Add(card => card.Load, load));
    Assert.Empty(component.FindAll(".dispatch-load__cycle"));
    Assert.Empty(component.FindAll("time"));

    load.Eta = complete with
    {
      CalculatedAt = clock.GetUtcNow().UtcDateTime,
      ValidUntil = clock.GetUtcNow().AddMinutes(2).UtcDateTime,
      Stops = [complete.Stops[^1] with { CycleAfterDeparture = Cycle(500) }],
      CycleAtCalculation = BaselineCycle() with { NextRecapMinutes = 60 },
    };
    component.Render(parameters => parameters.Add(card => card.Load, load));
    Assert.Contains("~8h 20m", component.Markup);
    Assert.DoesNotContain("+1h 00m", component.Markup);
    Assert.DoesNotContain("~12h 30m", component.Markup);
    Assert.DoesNotContain("+3h 05m", component.Markup);
  }

  [Fact]
  public void InitialPendingForecastShowsOnlyCompactEtaWithoutTechnicalReasonsOrEmptyCycleSections()
  {
    using var context = Context();
    var load = Load();
    const string reason =
      "ETA unavailable: waiting for the saved preceding connection.";
    load.Eta = load.Eta! with
    {
      Stops = [],
      RouteUpdatePending = true,
      CycleAtCalculation = null,
      UnavailableReason = reason,
    };
    var component = context.Render<DispatchLoadCard>(parameters =>
      parameters.Add(card => card.Load, load)
    );

    Assert.Equal(2, component.FindAll(".dispatch-load__eta-missing").Count);
    Assert.All(
      component.FindAll(".dispatch-load__eta-missing"),
      value => Assert.Null(value.GetAttribute("title"))
    );
    Assert.Empty(
      component.FindAll(".dispatch-load__stop-cycle, .dispatch-load__cycle")
    );
    Assert.DoesNotContain(reason, component.Markup);
    Assert.Empty(component.FindAll("time"));
    Assert.DoesNotContain("Updating", component.Markup);
    Assert.DoesNotContain("Previous", component.Markup);
  }

  [Fact]
  public void PendingForecastDoesNotKeepRecapWhoseReturnTimeHasPassed()
  {
    var clock = new FakeTimeProvider(Start);
    using var context = Context(clock);
    var load = Load();
    SetBaselineCycle(
      load,
      BaselineCycle() with
      {
        NextRecapAt = Start.AddMinutes(5).ToOffset(TimeSpan.FromHours(-4)),
      }
    );
    var component = context.Render<DispatchCycleForecast>(parameters =>
      parameters.Add(card => card.Load, load)
    );
    Assert.Empty(component.FindAll("time"));

    clock.Advance(TimeSpan.FromMinutes(6));
    load.Eta = load.Eta! with
    {
      Stops = [],
      RouteUpdatePending = true,
      CycleAtCalculation = null,
    };
    component.Render(parameters => parameters.Add(card => card.Load, load));

    Assert.Contains("~12h 30m", component.Markup);
    Assert.Empty(component.FindAll("time"));
    Assert.DoesNotContain("+3h 05m", component.Markup);
    Assert.Single(component.FindAll(".dispatch-load__cycle dd"));
    Assert.DoesNotContain("Next recap", component.Markup);
    Assert.Empty(component.FindAll(".dispatch-load__cycle-updating"));
    Assert.DoesNotContain("Updating", component.Markup);
  }

  [Fact]
  public void AnotherFinalStopOrLoadCannotInheritRetainedCycle()
  {
    using var context = Context();
    var load = Load();
    var component = context.Render<DispatchCycleForecast>(parameters =>
      parameters.Add(card => card.Load, load)
    );
    load.Stops.Add(
      new()
      {
        Id = Guid.NewGuid(),
        Sequence = 3,
        Job = "Delivery",
      }
    );
    load.Eta = load.Eta! with { Stops = [], RouteUpdatePending = true };
    component.Render(parameters => parameters.Add(card => card.Load, load));
    Assert.Empty(component.FindAll(".dispatch-load__cycle"));

    load = Load();
    component.Render(parameters => parameters.Add(card => card.Load, load));
    Assert.Contains("~12h 30m", component.Markup);
    load.Id = Guid.NewGuid();
    load.Eta = null;
    component.Render(parameters => parameters.Add(card => card.Load, load));
    Assert.Empty(component.FindAll(".dispatch-load__cycle"));
  }

  [Fact]
  public void CompletingTheFinalStopClearsItsForecast()
  {
    using var context = Context();
    var load = Load();
    var component = context.Render<DispatchCycleForecast>(parameters =>
      parameters.Add(card => card.Load, load)
    );
    load.Stops[^1].DeliveredAt = Start.UtcDateTime;
    load.Eta = load.Eta! with { RouteUpdatePending = true };
    component.Render(parameters => parameters.Add(card => card.Load, load));

    Assert.Empty(component.FindAll(".dispatch-load__cycle"));
    Assert.Empty(component.FindAll("time"));
    Assert.Empty(component.FindAll(".dispatch-load__cycle-updating"));
  }

  private static BunitContext Context(FakeTimeProvider? clock = null)
  {
    var context = new BunitContext();
    context.Services.AddSingleton<TimeProvider>(
      clock ?? new FakeTimeProvider(Start)
    );
    return context;
  }

  private static StopCycleForecast Cycle(int remaining = 750) =>
    new(
      remaining,
      new(2026, 9, 13, 0, 0, 0, TimeSpan.FromHours(-4)),
      798,
      "America/New_York",
      true
    );

  private static StopCycleForecast BaselineCycle() =>
    new(
      1400,
      new(2026, 9, 11, 0, 0, 0, TimeSpan.FromHours(-4)),
      185,
      "America/New_York",
      true
    );

  private static void SetBaselineCycle(
    DispatchResponse load,
    StopCycleForecast? cycle
  ) => load.Eta = load.Eta! with { CycleAtCalculation = cycle };

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
              CycleAfterDeparture = Cycle(index == 0 ? 60 : 750),
            }
        )
        .ToArray(),
      null,
      []
    )
    {
      CycleAtCalculation = BaselineCycle(),
    };
    return load;
  }
}
