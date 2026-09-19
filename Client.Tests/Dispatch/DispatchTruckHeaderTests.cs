using System.Net;
using System.Net.Http.Json;
using Bunit;
using Client.Models.DTO.Dispatch;
using Client.Models.DTO.Planning;
using Client.Pages.Dispatch;
using Client.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Client.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Component")]
public sealed class DispatchTruckHeaderTests
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
  public async Task AllViewsKeepTheSameOuterTitleToolbarAndUnframedBody()
  {
    using var context = new ClientComponentContext(
      (request, _) =>
        Task.FromResult(
          request.RequestUri!.AbsolutePath == "/api/dispatch/board"
            ? Json(
              new
              {
                items = Array.Empty<object>(),
                page = 1,
                pageSize = 20,
                totalCount = 0,
                totalPages = 1,
              }
            )
            : Json(Array.Empty<object>())
        )
    );
    context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Start));
    context.JSInterop.Mode = JSRuntimeMode.Loose;
    var component = context.Render<DispatchList>();

    foreach (var view in new[] { "Cards", "Table", "Papers", "Cards" })
    {
      await component.InvokeAsync(
        () =>
          component
            .FindAll(".dispatch-view button")
            .Single(button => button.TextContent == view)
            .ClickAsync(new())
      );
      component.WaitForAssertion(() =>
      {
        var page = component
          .Find(".dispatch-page > .page-header")
          .ParentElement!;
        var body = component.Find(".dispatch-board__body");
        Assert.Equal("dispatch-page dispatch-board", page.ClassName);
        Assert.Same(page, component.Find(".page-header").ParentElement);
        Assert.Same(
          page,
          component.Find(".dispatch-board__filters").ParentElement
        );
        Assert.Same(page, body.ParentElement);
        Assert.Equal("dispatch-board__body", body.ClassName);
        Assert.Empty(
          body.QuerySelectorAll(".page-header, .dispatch-board__filters")
        );
        Assert.Equal(
          "true",
          component
            .FindAll(".dispatch-view button")
            .Single(button => button.TextContent == view)
            .GetAttribute("aria-pressed")
        );
      });
    }
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task InitialLoadingAndErrorUseTheSameBodyAndMessageAcrossViewSwitches(
    bool failure
  )
  {
    var pending = new TaskCompletionSource<HttpResponseMessage>(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    using var context = new ClientComponentContext(
      (request, token) =>
        request.RequestUri!.AbsolutePath == "/api/dispatch/board"
          ? pending.Task.WaitAsync(token)
          : Task.FromResult(Json(Array.Empty<object>()))
    );
    context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Start));
    context.JSInterop.Mode = JSRuntimeMode.Loose;
    var component = context.Render<DispatchList>();
    component.WaitForAssertion(
      () =>
        Assert.Single(
          component.FindAll(".dispatch-board__message[role='status']")
        )
    );
    Assert.Empty(component.FindAll(".dispatch-truck__available--next"));
    var loadingMarkup = component.Find(".dispatch-board__message").OuterHtml;
    var header = component.Find(".page-header").OuterHtml;
    var switches = new List<Task>();
    foreach (var view in new[] { "Table", "Papers", "Cards" })
    {
      switches.Add(
        component.InvokeAsync(
          () =>
            component
              .FindAll(".dispatch-view button")
              .Single(button => button.TextContent == view)
              .ClickAsync(new())
        )
      );
      component.WaitForAssertion(() =>
      {
        Assert.Equal(
          "true",
          component
            .FindAll(".dispatch-view button")
            .Single(button => button.TextContent == view)
            .GetAttribute("aria-pressed")
        );
        Assert.Equal(
          "dispatch-board__body",
          component.Find(".dispatch-board__body").ClassName
        );
        Assert.Equal(
          loadingMarkup,
          component.Find(".dispatch-board__message").OuterHtml
        );
        Assert.Equal(header, component.Find(".page-header").OuterHtml);
      });
    }
    pending.SetResult(
      failure
        ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        : Json(
          new
          {
            items = Array.Empty<object>(),
            page = 1,
            pageSize = 20,
            totalCount = 0,
            totalPages = 1,
          }
        )
    );
    await Task.WhenAll(switches);
    component.WaitForAssertion(() =>
    {
      Assert.Equal(
        "dispatch-board__body",
        component.Find(".dispatch-board__body").ClassName
      );
      Assert.Empty(
        component.FindAll(".dispatch-board__message[role='status']")
      );
      Assert.Empty(component.FindAll(".dispatch-truck__available--next"));
      if (failure)
      {
        var error = component.Find(".dispatch-board__message[role='alert']");
        Assert.True(error.ClassList.Contains("dispatch-page__error"));
        Assert.Equal(
          "Retry",
          Assert.Single(error.QuerySelectorAll("button")).TextContent
        );
      }
      else
        Assert.Single(component.FindAll(".dispatch-board__empty"));
    });
  }

  [Theory]
  [InlineData(1, "in_transit", true, false, true)]
  [InlineData(2, "in_transit", true, false, false)]
  [InlineData(0, "in_transit", true, false, false)]
  [InlineData(1, "planned", true, false, false)]
  [InlineData(1, "in_transit", false, false, false)]
  [InlineData(1, "completed", true, true, false)]
  public async Task NextAssignmentPlaceholderOnlyFollowsALoneCurrentTruckLoad(
    int count,
    string status,
    bool assigned,
    bool archive,
    bool expected
  )
  {
    var load = Load();
    load.Status = status;
    load.Stops[0].ScheduledDate = DateOnly.FromDateTime(
      Start.AddDays(3).UtcDateTime
    );
    if (!assigned)
      load.TruckId = null;
    var loads = Enumerable
      .Range(0, count)
      .Select(index => index == 0 ? load : Load())
      .ToArray();
    using var context = new ClientComponentContext(
      (request, _) =>
        Task.FromResult(
          request.RequestUri!.AbsolutePath == "/api/dispatch/board"
            ? Json(
              new
              {
                items = new[]
                {
                  new
                  {
                    key = "truck",
                    truckId = load.TruckId,
                    truckNumber = "11006",
                    dispatches = loads,
                  },
                },
                page = 1,
                pageSize = 20,
                totalCount = 1,
                totalPages = 1,
              }
            )
          : request.RequestUri.AbsolutePath == "/api/dispatch"
            ? Json(
              new
              {
                items = loads,
                page = 1,
                pageSize = 20,
                totalCount = count,
                totalPages = 1,
              }
            )
          : Json(Array.Empty<object>())
        )
    );
    context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Start));
    context.JSInterop.Mode = JSRuntimeMode.Loose;
    var component = context.Render<DispatchList>();
    component.WaitForAssertion(
      () => Assert.Single(component.FindAll(".dispatch-truck"))
    );
    if (archive)
      await component.InvokeAsync(
        () => component.Find("#dispatch-completed").ClickAsync(new())
      );

    component.WaitForAssertion(() =>
    {
      Assert.Equal(count, component.FindAll(".dispatch-load").Count);
      var placeholders = component.FindAll(".dispatch-truck__available--next");
      Assert.Equal(expected ? 1 : 0, placeholders.Count);
      if (!expected)
        return;
      var placeholder = placeholders[0];
      Assert.Equal(
        "No next load",
        placeholder.QuerySelector("strong")!.TextContent
      );
      Assert.Equal("Next load", placeholder.GetAttribute("aria-label"));
      Assert.Equal("note", placeholder.GetAttribute("role"));
      Assert.True(
        placeholder.PreviousElementSibling!.ClassList.Contains(
          "dispatch-load--current"
        )
      );
      Assert.Empty(placeholder.QuerySelectorAll("button, a"));
    });
  }

  [Fact]
  public void TruckCardKeepsLiveSummaryWhileOnlyLoadStopsShowPlaceAppointmentEtaAndArrivalCycle()
  {
    var load = Load();
    var planning = Result(load);
    using var context = new ClientComponentContext(
      (request, _) =>
        Task.FromResult(
          request.RequestUri!.AbsolutePath == "/api/dispatch/board"
            ? Json(
              new
              {
                items = new[]
                {
                  new
                  {
                    key = load.TruckId.ToString(),
                    truckId = load.TruckId,
                    truckNumber = "11005",
                    driverName = "Test Driver",
                    hos = Clocks(),
                    currentCycle = new DriverCycleSnapshot(
                      Start.UtcDateTime,
                      Start.AddMinutes(2).UtcDateTime,
                      new(1400, Start.AddDays(1), 185, "UTC", true)
                    ),
                    dispatches = new[] { load },
                  },
                },
                page = 1,
                pageSize = 20,
                totalCount = 1,
                totalPages = 1,
              }
            )
          : request.RequestUri.AbsolutePath == "/api/dispatch/board/planning"
            ? Json(new[] { planning })
          : request.RequestUri.AbsolutePath == "/api/fleet/locations"
            ? Json(
              new
              {
                trucks = Array.Empty<object>(),
                points = Array.Empty<object>(),
              }
            )
          : Json(Array.Empty<object>())
        )
    );
    context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Start));
    context.JSInterop.Mode = JSRuntimeMode.Loose;
    var component = context.Render<DispatchList>();

    component.WaitForAssertion(() =>
    {
      var truckHeader = component.Find(".dispatch-truck__header");
      Assert.Contains("11005", truckHeader.TextContent);
      Assert.Contains("Test Driver", truckHeader.TextContent);
      Assert.NotNull(truckHeader.QuerySelector(".dispatch-truck__icon"));
      Assert.Empty(component.FindAll(".dispatch-rig"));
      var summary = component.Find(".dispatch-truck__equipment");
      Assert.Empty(
        summary.QuerySelectorAll(
          ".dispatch-planning__details, .dispatch-planning__next, .arrival-estimate, .stop-hours__cycle"
        )
      );
      Assert.DoesNotContain("Middletown", summary.TextContent);
      Assert.DoesNotContain("Cycle remaining", summary.TextContent);
      Assert.Contains("Fuel 50%", summary.TextContent);
      Assert.Empty(summary.QuerySelectorAll("details"));
      var distances = summary.QuerySelector(".dispatch-planning__metrics")!;
      Assert.Contains("100 mi", distances.TextContent);
      Assert.Contains("75 mi", distances.TextContent);
      Assert.Contains("Next Stop", distances.TextContent);
      var clocks = Assert.Single(summary.QuerySelectorAll(".driver-hours"));
      foreach (
        var value in new[]
        {
          "Break",
          "Drive",
          "Shift",
          "Cycle",
          "4:00",
          "5:00",
          "6:00",
          "20:00",
        }
      )
        Assert.Contains(value, clocks.TextContent);
      Assert.Contains(
        "Driving",
        summary.QuerySelector(".driver-duty__current")!.TextContent
      );
      Assert.Empty(
        summary.QuerySelectorAll(".driver-hours-panel .driver-duty")
      );
      var recap = Assert.Single(summary.QuerySelectorAll(".driver-next-recap"));
      Assert.Contains("+3h 05m", recap.TextContent);
      Assert.Null(recap.Closest("details"));
      Assert.Null(clocks.Closest("details"));
      Assert.Null(
        summary.QuerySelector(".driver-duty__current")!.Closest("details")
      );
      Assert.DoesNotContain("Driver details", summary.TextContent);
      Assert.DoesNotContain(
        "Next recap",
        component.Find(".dispatch-truck__loads").TextContent
      );

      var stop = component.Find(".dispatch-truck__loads .dispatch-load__stop");
      Assert.Contains(
        "Middletown, DE",
        stop.QuerySelector(".dispatch-load__location")!.TextContent
      );
      Assert.Contains(
        "Delivery",
        stop.QuerySelector(".arrival-estimate__appointment")!.TextContent
      );
      Assert.Contains(
        "Sep 8",
        stop.QuerySelector(".arrival-estimate__appointment")!.TextContent
      );
      Assert.Contains(
        "01:00 PM",
        stop.QuerySelector(".stop-hours__road")!.TextContent
      );
      Assert.Null(stop.QuerySelector(".stop-hours__cycle"));
      Assert.NotNull(component.Find(".dispatch-load__details"));
    });
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public void CompactSummaryKeepsDutyRecapClocksAndDistancesVisibleWithoutDisclosures(
    bool boardHeader
  )
  {
    var load = Load();
    using var context = new ClientComponentContext(
      (_, _) => Task.FromResult(Json(Result(load)))
    );
    context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Start));
    var component = context.Render<DispatchPlanning>(parameters =>
      parameters
        .Add(part => part.Load, load)
        .Add(part => part.TruckId, load.TruckId)
        .Add(part => part.Compact, true)
        .Add(part => part.BoardHeader, boardHeader)
        .Add(part => part.Hos, Clocks())
    );

    component.WaitForAssertion(() =>
    {
      Assert.Empty(component.FindAll("details"));
      Assert.Contains(
        "Driving",
        component.Find(".driver-duty__current").TextContent
      );
      Assert.Single(component.FindAll(".driver-duty__label"));
      Assert.Contains("HOS", component.Find(".driver-duty__label").TextContent);
      Assert.Equal(3, component.FindAll(".dispatch-planning__metric").Count);
      Assert.Null(component.Find(".driver-next-recap").Closest("details"));
      Assert.Null(component.Find(".driver-hours").Closest("details"));
      Assert.Equal(4, component.FindAll(".driver-hours__clock").Count);
    });
  }

  [Fact]
  public void TruckWithoutActiveLoadsKeepsClocksStatusAndQuietRecapWithoutRequestingAPlan()
  {
    var truckId = Guid.NewGuid();
    var planningRequests = 0;
    using var context = new ClientComponentContext(
      (request, _) =>
      {
        var path = request.RequestUri!.AbsolutePath;
        if (path == "/api/dispatch/board/planning")
          planningRequests++;
        return Task.FromResult(
          path == "/api/dispatch/board"
            ? Json(
              new
              {
                items = new[]
                {
                  new
                  {
                    key = truckId.ToString(),
                    truckId,
                    truckNumber = "11005",
                    hos = Clocks(),
                    dispatches = Array.Empty<DispatchResponse>(),
                  },
                },
                page = 1,
                pageSize = 20,
                totalCount = 1,
                totalPages = 1,
              }
            )
          : path == "/api/fleet/locations"
            ? Json(
              new
              {
                trucks = Array.Empty<object>(),
                points = Array.Empty<object>(),
              }
            )
          : Json(Array.Empty<object>())
        );
      }
    );
    context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Start));
    context.JSInterop.Mode = JSRuntimeMode.Loose;
    var component = context.Render<DispatchList>();

    component.WaitForAssertion(() =>
    {
      Assert.Equal(
        "—",
        component.Find(".driver-next-recap strong").TextContent.Trim()
      );
      Assert.Equal(4, component.FindAll(".driver-hours__clock").Count);
      Assert.Contains(
        "Driving",
        component.Find(".dispatch-planning__driver").TextContent
      );
      Assert.Empty(component.FindAll(".driver-hours-panel .driver-duty"));
      Assert.Equal(0, planningRequests);
    });
  }

  [Fact]
  public async Task TelemetryCannotRelabelTheBoardDriverHoursAndRecapBeforeTheirReplacementArrives()
  {
    var load = Load();
    var clock = new FakeTimeProvider(Start);
    var boardReads = 0;
    var fleetReads = 0;
    using var context = new ClientComponentContext(
      (request, _) =>
      {
        var path = request.RequestUri!.AbsolutePath;
        if (path == "/api/dispatch/board")
        {
          if (
            !request.RequestUri.Query.Contains("includeEta=true")
            && !request.RequestUri.Query.Contains("includeFinancials=true")
          )
            boardReads++;
          var replacement = boardReads > 1;
          var hos = Clocks();
          hos.CycleMs = (replacement ? 10 : 20) * 3600000;
          hos.CurrentDutyStatus = replacement ? "offDuty" : "driving";
          hos.UpdatedAt = clock.GetUtcNow().UtcDateTime;
          return Task.FromResult(
            Json(
              new
              {
                items = new[]
                {
                  new
                  {
                    key = load.TruckId.ToString(),
                    truckId = load.TruckId,
                    truckNumber = "11005",
                    driverName = replacement ? "Next Driver" : "Board Driver",
                    hos,
                    currentCycle = new DriverCycleSnapshot(
                      clock.GetUtcNow().UtcDateTime,
                      clock.GetUtcNow().AddMinutes(2).UtcDateTime,
                      new(
                        1400,
                        Start.AddDays(1),
                        replacement ? 60 : 185,
                        "UTC",
                        true
                      )
                    ),
                    dispatches = new[] { load },
                  },
                },
                page = 1,
                pageSize = 20,
                totalCount = 1,
                totalPages = 1,
              }
            )
          );
        }
        if (path == "/api/dispatch/board/telemetry")
          return Task.FromResult(
            Json(
              new[]
              {
                new
                {
                  truckId = load.TruckId,
                  speed = ++fleetReads > 1 ? 25 : 0,
                  engineState = "On",
                  trailerNumber = "TR-200",
                },
              }
            )
          );
        return Task.FromResult(
          path == "/api/dispatch/board/planning"
            ? Json(new[] { Result(load) })
            : Json(Array.Empty<object>())
        );
      }
    );
    context.Services.AddSingleton<TimeProvider>(clock);
    context.JSInterop.Mode = JSRuntimeMode.Loose;
    var component = context.Render<DispatchList>();
    component.WaitForAssertion(() =>
    {
      Assert.True(fleetReads >= 1);
      Assert.Equal(
        "Board Driver",
        component.Find(".dispatch-truck__header-driver").TextContent
      );
      Assert.Contains("20:00", component.Find(".driver-hours").TextContent);
      Assert.Contains(
        "+3h 05m",
        component.Find(".driver-next-recap").TextContent
      );
    });

    await component.InvokeAsync(() => clock.Advance(TimeSpan.FromSeconds(10)));
    component.WaitForAssertion(() =>
    {
      Assert.True(fleetReads >= 2);
      Assert.Equal(1, boardReads);
      Assert.Equal(
        "Board Driver",
        component.Find(".dispatch-truck__header-driver").TextContent
      );
      Assert.Contains(
        "25 mph",
        component.Find(".dispatch-truck__status").TextContent
      );
      Assert.Contains("20:00", component.Find(".driver-hours").TextContent);
      Assert.Contains(
        "+3h 05m",
        component.Find(".driver-next-recap").TextContent
      );
    });

    await component.InvokeAsync(() => clock.Advance(TimeSpan.FromSeconds(51)));
    component.WaitForAssertion(() =>
    {
      Assert.Equal(2, boardReads);
      Assert.Equal(
        "Next Driver",
        component.Find(".dispatch-truck__header-driver").TextContent
      );
      Assert.Contains("10:00", component.Find(".driver-hours").TextContent);
      Assert.DoesNotContain(
        "20:00",
        component.Find(".driver-hours").TextContent
      );
      Assert.Contains(
        "Off Duty",
        component.Find(".dispatch-planning__driver").TextContent
      );
      Assert.Contains(
        "+1h 00m",
        component.Find(".driver-next-recap").TextContent
      );
      Assert.DoesNotContain(
        "+3h 05m",
        component.Find(".driver-next-recap").TextContent
      );
    });
  }

  [Fact]
  public void NonCompactRouteSummaryKeepsItsStopDetails()
  {
    var load = Load();
    using var context = new ClientComponentContext(
      (_, _) => Task.FromResult(Json(Result(load)))
    );
    context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Start));
    var component = context.Render<DispatchPlanning>(parameters =>
      parameters
        .Add(part => part.Load, load)
        .Add(part => part.TruckId, load.TruckId)
    );

    component.WaitForAssertion(() =>
    {
      var details = component.Find(".dispatch-planning__details");
      Assert.Contains("Delivery · Middletown, DE", details.TextContent);
      Assert.NotNull(details.QuerySelector(".arrival-estimate__appointment"));
      Assert.Contains(
        "01:00 PM",
        details.QuerySelector(".stop-hours__road")!.TextContent
      );
      Assert.Contains(
        "Cycle remaining",
        details.QuerySelector(".stop-hours__cycle")!.TextContent
      );
    });
  }

  [Theory]
  [InlineData(0)]
  [InlineData(60)]
  [InlineData(null)]
  public void FuelReadingShowsOnlyThePercentageRegardlessOfReadingAge(
    int? ageMinutes
  )
  {
    var load = Load();
    var result = Result(load);
    result = result with
    {
      State = result.State! with
      {
        FuelPercent = 46,
        FuelUpdatedAt = ageMinutes.HasValue
          ? Start.AddMinutes(-ageMinutes.Value).UtcDateTime
          : null,
      },
    };
    using var context = new ClientComponentContext(
      (_, _) => Task.FromResult(Json(result))
    );
    context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Start));
    var component = context.Render<DispatchPlanning>(parameters =>
      parameters
        .Add(part => part.Load, load)
        .Add(part => part.TruckId, load.TruckId)
    );

    component.WaitForAssertion(
      () =>
        Assert.Equal(
          "Fuel 46%",
          component.Find(".dispatch-planning__fuel").TextContent.Trim()
        )
    );
  }

  [Theory]
  [InlineData(65, "is-normal", true)]
  [InlineData(67, "is-low", true)]
  [InlineData(70, "is-low", true)]
  [InlineData(71, "is-critical", true)]
  [InlineData(67, "is-low", false)]
  public void MovingBadgeUsesSpeedThresholdsWithOrWithoutAnAssignedLoad(
    int speed,
    string tone,
    bool assigned
  )
  {
    var load = Load();
    using var context = new ClientComponentContext(
      (request, _) =>
        Task.FromResult(
          request.RequestUri!.AbsolutePath == "/api/dispatch/board"
            ? Json(
              new
              {
                items = new[]
                {
                  new
                  {
                    key = load.TruckId.ToString(),
                    truckId = load.TruckId,
                    truckNumber = "11007",
                    speed,
                    engineState = "On",
                    dispatches = assigned
                      ? new[] { load }
                      : Array.Empty<DispatchResponse>(),
                  },
                },
                page = 1,
                pageSize = 20,
                totalCount = 1,
                totalPages = 1,
              }
            )
          : request.RequestUri.AbsolutePath == "/api/dispatch/board/planning"
            ? Json(new[] { Result(load) })
          : request.RequestUri.AbsolutePath == "/api/fleet/locations"
            ? Json(
              new
              {
                trucks = Array.Empty<object>(),
                points = Array.Empty<object>(),
              }
            )
          : Json(Array.Empty<object>())
        )
    );
    context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Start));
    context.JSInterop.Mode = JSRuntimeMode.Loose;
    var component = context.Render<DispatchList>();
    component.WaitForAssertion(() =>
    {
      var badge = component.Find(".dispatch-truck__status.is-moving");
      Assert.True(badge.ClassList.Contains(tone));
      Assert.Contains($"Driving · {speed} mph", badge.TextContent);
    });
  }

  private static DispatchResponse Load() =>
    new()
    {
      Id = Guid.NewGuid(),
      TruckId = Guid.NewGuid(),
      LoadNumber = 1370,
      Status = "in_transit",
      Stops =
      [
        new()
        {
          Id = Guid.NewGuid(),
          Sequence = 1,
          Job = "Delivery",
          City = "Middletown",
          Province = "DE",
          Address = "100 Warehouse Road",
          ScheduledDate = new(2026, 9, 8),
          ScheduledTime = new(14, 0),
        },
      ],
    };

  private static DriverHosClocks Clocks() =>
    new()
    {
      BreakMs = 4 * 3600000,
      DriveMs = 5 * 3600000,
      ShiftMs = 6 * 3600000,
      CycleMs = 20 * 3600000,
      CurrentDutyStatus = "driving",
      UpdatedAt = Start.UtcDateTime,
    };

  private static AutomaticPlanningResult Result(DispatchResponse load)
  {
    var stop = load.Stops[0];
    var plan = new RoutePlan
    {
      Id = Guid.NewGuid(),
      DispatchId = load.Id,
      TruckId = load.TruckId!.Value,
      Version = 1,
      OriginalPlannedMiles = 100,
      Stops = [new(stop.Id, stop.Job, stop.Address, 1, new(40, -80))],
      Tracking = new()
      {
        NextStopId = stop.Id,
        NextStopLabel = "Delivery · Middletown, DE",
      },
    };
    var eta = new DispatchEta(
      Start.UtcDateTime,
      Start.AddMinutes(2).UtcDateTime,
      [
        new(stop.Id, Start.AddHours(1), "UTC", Start.AddHours(2), 0, 60, 0)
        {
          DispatchId = load.Id,
          Hours = new(600, 480, null, null, true, [], null),
        },
      ],
      null,
      []
    );
    return new(
      load.TruckId.Value,
      load.Id,
      load.LoadNumber,
      new(
        new(),
        plan,
        new(25, 75, 3600, 0, false, false, Start.UtcDateTime, null),
        50,
        Start.UtcDateTime,
        true
      )
      {
        Eta = eta,
      },
      null
    );
  }

  private static HttpResponseMessage Json(object value) =>
    new(HttpStatusCode.OK)
    {
      Content = JsonContent.Create(new { success = true, response = value }),
    };
}
