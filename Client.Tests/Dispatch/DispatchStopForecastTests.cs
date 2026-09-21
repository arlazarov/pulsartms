using Bunit;
using Client.Models.DTO.Dispatch;
using Client.Models.DTO.Planning;
using Client.Pages.Dispatch;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Client.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Component")]
public sealed class DispatchStopForecastTests
{
  private static readonly DateTimeOffset Now = new(
    2026,
    9,
    14,
    12,
    0,
    0,
    TimeSpan.Zero
  );

  [Fact]
  public void ExactSavedStopOwnsItsArrivalAndCycle()
  {
    using var context = Context();
    var load = Load();
    var estimate = load.Eta!.Stops[0];
    load.Eta = load.Eta with
    {
      Stops =
      [
        estimate with
        {
          DispatchId = Guid.NewGuid(),
          Arrival = Now.AddDays(7),
          Hours = Hours(1),
        },
        estimate with
        {
          StopId = Guid.NewGuid(),
          Hours = Hours(2),
        },
        estimate,
      ],
    };

    var component = Render(context, load);

    Assert.Equal("02:00 PM", component.Find(".stop-hours__clock").TextContent);
    Assert.Empty(component.FindAll(".stop-hours__cycle"));
    component.Render(p => p.Add(x => x.Detailed, true));
    Assert.Empty(component.FindAll(".stop-hours__road"));
    Assert.Equal(
      "+10h 00m",
      component.Find(".stop-hours__arrival-cycle strong").TextContent
    );
  }

  [Theory]
  [InlineData("dispatch")]
  [InlineData("stop")]
  [InlineData("empty-dispatch")]
  [InlineData("empty-stop")]
  [InlineData("expired")]
  public void InvalidOrExpiredIdentityCannotDisplaySavedForecast(string kind)
  {
    using var context = Context();
    var load = Load();
    if (kind == "dispatch")
      load.Id = Guid.NewGuid();
    if (kind == "stop")
      load.Stops[0].Id = Guid.NewGuid();
    if (kind == "empty-dispatch")
      load.Id = Guid.Empty;
    if (kind == "empty-stop")
      load.Stops[0].Id = Guid.Empty;
    if (kind == "expired")
      load.Eta = load.Eta! with { ValidUntil = Now.UtcDateTime };

    var component = Render(context, load);

    Assert.Contains("ETA —", component.Markup);
    Assert.Empty(component.FindAll(".arrival-estimate"));
  }

  [Fact]
  public void DraftChangesHideSavedForecastUntilDiscardOrSave()
  {
    using var context = Context();
    var component = Render(context, Load());

    component.Render(p => p.Add(x => x.DraftChanged, true));
    Assert.Contains("ETA updates after save", component.Markup);
    Assert.Empty(component.FindAll(".arrival-estimate"));
    component.Render(p => p.Add(x => x.Detailed, true));
    Assert.Empty(component.FindAll(".stop-hours__cycle"));
    component.Render(p => p.Add(x => x.DraftChanged, false));
    Assert.Equal(
      "+10h 00m",
      component.Find(".stop-hours__arrival-cycle strong").TextContent
    );
  }

  [Fact]
  public void CompletedAndDriverOnlyStopsCannotReceiveTruckForecasts()
  {
    using var context = Context();
    var load = Load();
    load.Stops[0].DriverOnly = true;
    load.Status = "completed";
    load.Completed = true;
    var component = Render(context, load);
    Assert.Contains("Driver only · No truck", component.Markup);
    Assert.DoesNotContain("Completed", component.Markup);
    Assert.Empty(component.FindAll(".arrival-estimate"));

    load.Stops[0].DriverOnly = false;
    load.Stops[0].ExecutionCompleted = true;
    load.Stops[0].IsCompleted = true;
    component.Render(p => p.Add(x => x.Load, load));

    Assert.Contains("Completed", component.Markup);
    Assert.Empty(component.FindAll(".arrival-estimate"));
  }

  [Fact]
  public void LegacyCycleRemainsAvailableWithoutInventedArrivalBalance()
  {
    using var context = Context();
    var load = Load();
    load.Eta = load.Eta! with
    {
      Stops =
      [
        load.Eta.Stops[0] with
        {
          Hours = null,
          CycleAfterDeparture = new(750, null, null, null, false),
        },
      ],
    };
    var component = Render(context, load);
    component.Render(p => p.Add(x => x.Detailed, true));

    Assert.Equal(
      "~12h 30m",
      component.Find(".stop-workspace__legacy-cycle strong").TextContent
    );
    Assert.Contains("Cycle after stop", component.Markup);
    Assert.Empty(component.FindAll(".stop-hours__arrival-cycle"));
  }

  private static IRenderedComponent<DispatchStopForecast> Render(
    BunitContext context,
    DispatchResponse load
  ) =>
    context.Render<DispatchStopForecast>(p =>
      p.Add(x => x.Load, load).Add(x => x.StopId, load.Stops[0].Id)
    );

  private static BunitContext Context()
  {
    var context = new BunitContext();
    context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
    return context;
  }

  private static StopHoursForecast Hours(int cycle) =>
    new(cycle, cycle - 60, null, null, true, [], null);

  private static DispatchResponse Load()
  {
    var load = new DispatchResponse
    {
      Id = Guid.NewGuid(),
      Stops =
      [
        new()
        {
          Id = Guid.NewGuid(),
          Sequence = 1,
          Name = "Destination",
          Job = "Delivery",
          ScheduledDate = new(2026, 9, 14),
          ScheduledTime = new(15, 0),
        },
      ],
    };
    load.Eta = new(
      Now.UtcDateTime,
      Now.AddMinutes(5).UtcDateTime,
      [
        new(load.Stops[0].Id, Now.AddHours(2), "Etc/UTC", null, 0, 120, 0)
        {
          DispatchId = load.Id,
          Hours = Hours(600),
        },
      ],
      null,
      []
    );
    return load;
  }
}
