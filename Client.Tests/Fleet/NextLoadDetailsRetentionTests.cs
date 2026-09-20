using Bunit;
using Client.Models.DTO.Dispatch;
using Client.Models.DTO.Planning;
using Client.Pages.Dispatch;
using Client.Pages.FleetMap;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Client.Tests.Fleet;

[Trait("Category", "Fleet")]
[Trait("Kind", "Component")]
public sealed class NextLoadDetailsRetentionTests
{
  [Fact]
  public void DisclosureKeepsSummaryAndForecastMemoryWhileHidingExtraFacts()
  {
    using var context = new BunitContext();
    var clock = new FakeTimeProvider();
    context.Services.AddSingleton<TimeProvider>(clock);
    var (route, stop, eta) = Inputs(clock);
    eta = eta with
    {
      Stops =
      [
        eta.Stops[0] with
        {
          Hours = new(300, 180, 0, null, true, [], null),
        },
      ],
    };
    var component = context.Render<NextLoadDetailsCard>(p =>
      p.Add(x => x.Route, route)
        .Add(x => x.Stop, stop)
        .Add(x => x.Eta, eta)
        .Add(x => x.Expanded, false)
    );
    var summary = component.Find(".stop-hours__road").OuterHtml;
    Assert.Empty(component.FindAll(".stop-hours__cycle"));
    Assert.True(
      component
        .Find(".fleet-map-next-load-card__metrics")
        .HasAttribute("hidden")
    );
    Assert.True(
      component
        .Find(".fleet-map-next-load-card__assignment")
        .HasAttribute("hidden")
    );
    Assert.Single(component.FindAll(".fleet-route-popup__fuel"));

    component.Render(p => p.Add(x => x.Expanded, true));
    Assert.Equal(summary, component.Find(".stop-hours__road").OuterHtml);
    Assert.Contains(
      "+5h 00m",
      component.Find(".stop-hours__cycle").TextContent
    );
    Assert.False(
      component
        .Find(".fleet-map-next-load-card__metrics")
        .HasAttribute("hidden")
    );
    clock.Advance(TimeSpan.FromMinutes(3));
    component.Render(p => p.Add(x => x.Expanded, false).Add(x => x.Eta, null));
    component.Render(p => p.Add(x => x.Expanded, true));
    Assert.Equal(summary, component.Find(".stop-hours__road").OuterHtml);
    Assert.Contains(
      "+5h 00m",
      component.Find(".stop-hours__cycle").TextContent
    );
  }

  [Fact]
  public void RepeatVisitsUseExactStopIdentityAndNeverCarryAcrossLoadsOrMissingDetails()
  {
    using var context = new BunitContext();
    context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider());
    var details = new DispatchResponse
    {
      Id = Guid.NewGuid(),
      Stops = Enumerable
        .Range(1, 5)
        .Select(number => new DispatchStopResponse
        {
          Id = Guid.NewGuid(),
          Sequence = number,
          Job = number == 5 ? "Delivery" : "Pickup",
          Address = number is 1 or 3 or 4
            ? "675 Basket Rd"
            : $"{number} Main St",
          City =
            number == 5 ? "Port St Lucie"
            : number == 2 ? "Amsterdam"
            : "Webster",
          Province = number == 5 ? "FL" : "NY",
          Country = "US",
          ScheduledDate = new(2026, 9, 11),
          ScheduledTime = new(
            number switch
            {
              1 => 2,
              2 => 11,
              3 => 13,
              4 => 14,
              _ => 5,
            },
            0
          ),
        })
        .ToList(),
    };
    var selected = details.Stops[3];
    var stop = new PlanStop(
      selected.Id,
      "Warehouse",
      "675 Basket Rd, Webster, NY, US",
      4,
      new(43.2, -77.4)
    )
    {
      Job = selected.Job,
      ScheduledDate = selected.ScheduledDate,
      ScheduledTime = selected.ScheduledTime,
    };
    var route = new NextLoadRoute(
      details.Id,
      1383,
      "ready",
      [],
      details
        .Stops.Select(value => new NextLoadStop(43.2, -77.4, value.Job)
        {
          Id = value.Id,
        })
        .ToArray(),
      StopCount: 5
    );
    var component = context.Render<NextLoadDetailsCard>(p =>
      p.Add(x => x.Route, route)
        .Add(x => x.Stop, stop)
        .Add(x => x.StopIndex, 3)
        .Add(x => x.Details, details)
    );

    Assert.Contains(
      "Load stop 4 of 5",
      component.Find(".fleet-route-popup__kind").TextContent
    );
    Assert.Equal(
      "Visit 3 of 3",
      component.Find(".fleet-route-popup__visit").TextContent
    );
    Assert.Equal(
      "Visit 3 of 3 at this address",
      component.Find(".fleet-route-popup__visit").GetAttribute("title")
    );
    Assert.Contains(
      "02:00 PM",
      component.Find(".fleet-route-popup__appointment").TextContent
    );
    component.Render(p =>
      p.Add(x => x.StopIndex, 2)
        .Add(
          x => x.Stop,
          stop with
          {
            Id = details.Stops[2].Id,
            Sequence = 3,
            ScheduledTime = new(13, 0),
          }
        )
    );
    Assert.Equal(
      "Visit 2 of 3",
      component.Find(".fleet-route-popup__visit").TextContent
    );
    Assert.Contains(
      "01:00 PM",
      component.Find(".fleet-route-popup__appointment").TextContent
    );
    component.Render(p =>
      p.Add(
        x => x.Details,
        new DispatchResponse { Id = Guid.NewGuid(), Stops = details.Stops }
      )
    );
    Assert.Empty(component.FindAll(".fleet-route-popup__visit"));
    component.Render(p => p.Add(x => x.Details, (DispatchResponse?)null));
    Assert.Empty(component.FindAll(".fleet-route-popup__visit"));
  }

  [Fact]
  public void FuelArrivalIsAlwaysVisibleAndMustMatchTheExactLoadAndStop()
  {
    using var context = new BunitContext();
    var clock = new FakeTimeProvider();
    context.Services.AddSingleton<TimeProvider>(clock);
    var (route, stop, _) = Inputs(clock);
    var arrival = new FuelStopArrival(route.Id, stop.Id, 82, 32.8);
    var component = context.Render<NextLoadDetailsCard>(p =>
      p.Add(x => x.Route, route)
        .Add(x => x.Stop, stop)
        .Add(x => x.FuelArrival, arrival)
    );
    Assert.Single(
      component.FindAll(
        ".fleet-route-popup__information .fleet-route-popup__fuel"
      )
    );
    Assert.Empty(
      component.FindAll(".fleet-route-popup__location .fleet-route-popup__fuel")
    );
    // The card says fuel as a named figure on its line, on the same label
    // column as the appointment above it, not as a dial in its own block.
    var fuel = () =>
      component
        .Find(".fleet-route-popup__fuel .fleet-route-popup__value")
        .TextContent;
    Assert.Equal(
      "Fuel on arrival",
      component
        .Find(".fleet-route-popup__fuel .fleet-route-popup__label")
        .TextContent.Trim()
    );
    Assert.Equal("33% · 82 US gal", fuel());
    Assert.Empty(component.FindAll(".driver-hours__arc"));
    component.Render(p =>
      p.Add(x => x.FuelArrival, arrival with { StopId = Guid.NewGuid() })
    );
    Assert.Equal("—", fuel());
    component.Render(p =>
      p.Add(x => x.FuelArrival, arrival with { DispatchId = Guid.NewGuid() })
    );
    Assert.Equal("—", fuel());
    component.Render(p => p.Add(x => x.FuelArrival, null));
    Assert.Equal("—", fuel());
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public void UnavailableFutureStopShowsOneCompactPlaceholderWithoutTheTechnicalReason(
    bool pending
  )
  {
    using var context = new BunitContext();
    var clock = new FakeTimeProvider();
    context.Services.AddSingleton<TimeProvider>(clock);
    var (route, stop, eta) = Inputs(clock);
    const string reason =
      "ETA unavailable: waiting for the saved connection from the preceding load.";
    eta = eta with
    {
      Stops = [],
      UnavailableReason = reason,
      RouteUpdatePending = pending,
    };
    var component = context.Render<NextLoadDetailsCard>(p =>
      p.Add(x => x.Route, route).Add(x => x.Stop, stop).Add(x => x.Eta, eta)
    );

    Assert.Equal(
      "ETA —",
      Assert.Single(component.FindAll(".fleet-route-popup__eta")).TextContent
    );
    Assert.Empty(component.FindAll(".arrival-estimate, .stop-hours__cycle"));
    Assert.DoesNotContain(reason, component.Markup);
  }

  [Fact]
  public void FutureSelectedStopKeepsRoadEtaHoursAndNearestRecapUntilTheCompleteReplacement()
  {
    using var context = new BunitContext();
    var clock = new FakeTimeProvider(
      new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)
    );
    context.Services.AddSingleton<TimeProvider>(clock);
    var (route, stop, eta) = Inputs(clock);
    eta = eta with
    {
      Stops =
      [
        eta.Stops[0] with
        {
          Appointment = clock.GetUtcNow().AddHours(7),
          Hours = new(
            -60,
            -180,
            60,
            clock.GetUtcNow(),
            true,
            [
              new(
                "recap",
                clock.GetUtcNow().AddHours(6),
                clock.GetUtcNow().AddHours(8),
                0,
                300,
                null,
                null
              ),
            ],
            null
          ),
        },
      ],
      CycleAtCalculation = new(
        400,
        clock.GetUtcNow().AddDays(3),
        185,
        "UTC",
        true
      ),
    };
    var component = context.Render<NextLoadDetailsCard>(p =>
      p.Add(x => x.Route, route).Add(x => x.Stop, stop).Add(x => x.Eta, eta)
    );
    var previousMarkup = component.Find(".arrival-estimate").OuterHtml;
    clock.Advance(TimeSpan.FromMinutes(3));
    component.Render(p =>
      p.Add(
        x => x.Eta,
        eta with
        {
          Stops = [],
          RouteUpdatePending = true,
          CycleAtCalculation = null,
        }
      )
    );
    Assert.Equal(
      "ETA",
      component.Find(".stop-hours__road .stop-hours__label").TextContent
    );
    Assert.Contains(
      "−1h 00m",
      component.Find(".stop-hours__arrival-cycle").TextContent
    );
    Assert.Empty(component.FindAll(".stop-hours__departure-cycle"));
    Assert.Contains(
      "+3h 05m",
      component.Find(".stop-hours__recap").TextContent
    );
    Assert.Equal(previousMarkup, component.Find(".arrival-estimate").OuterHtml);
    Assert.Contains(
      "Cycle short",
      component.Find(".stop-hours__status--danger").TextContent
    );
    Assert.DoesNotContain("Previous", component.Markup);
    Assert.DoesNotContain("Updating", component.Markup);
    Assert.Single(component.FindAll(".stop-hours__alternative"));
    Assert.Single(component.FindAll(".stop-hours__status--success"));
    Assert.Empty(component.FindAll(".fleet-route-popup__eta"));

    var refreshed = eta with
    {
      CalculatedAt = clock.GetUtcNow().UtcDateTime,
      ValidUntil = clock.GetUtcNow().AddMinutes(2).UtcDateTime,
      Stops =
      [
        eta.Stops[0] with
        {
          Hours = new(300, 180, 0, null, true, [], null),
        },
      ],
    };
    component.Render(p => p.Add(x => x.Eta, refreshed));
    Assert.Equal(
      "+5h 00m",
      component.Find(".stop-hours__arrival-cycle strong").TextContent
    );
    Assert.Equal(
      "On time",
      component.Find(".stop-hours__status--success").TextContent
    );
    Assert.Empty(
      component.FindAll(".stop-hours__status--danger, .stop-hours__alternative")
    );
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public void MissingOrPendingEtaDoesNotAddADashBesideTheRetainedEstimate(
    bool pending
  )
  {
    using var context = new BunitContext();
    var clock = new FakeTimeProvider(
      new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)
    );
    context.Services.AddSingleton<TimeProvider>(clock);
    var (route, stop, eta) = Inputs(clock);
    var component = context.Render<NextLoadDetailsCard>(p =>
      p.Add(x => x.Route, route).Add(x => x.Stop, stop).Add(x => x.Eta, eta)
    );
    Assert.Single(component.FindAll(".arrival-estimate"));
    Assert.Empty(component.FindAll(".fleet-route-popup__eta"));
    var previousMarkup = component.Find(".arrival-estimate").OuterHtml;

    clock.Advance(TimeSpan.FromMinutes(3));
    var incoming = pending
      ? eta with
      {
        Stops = [],
        RouteUpdatePending = true,
      }
      : null;
    component.Render(p => p.Add(x => x.Eta, incoming));
    Assert.Contains(
      "Sep 8 · 01:00 PM",
      component.Find(".arrival-estimate").TextContent
    );
    Assert.Equal(previousMarkup, component.Find(".arrival-estimate").OuterHtml);
    Assert.DoesNotContain("Updating", component.Markup);
    Assert.Empty(component.FindAll(".fleet-route-popup__eta"));

    clock.Advance(TimeSpan.FromMinutes(15));
    component.Render();
    Assert.Empty(component.FindAll(".arrival-estimate"));
    Assert.Equal(
      "ETA —",
      component.Find(".fleet-route-popup__eta").TextContent
    );
  }

  [Theory]
  [InlineData("stop")]
  [InlineData("load")]
  [InlineData("completed")]
  public void DifferentOrCompletedStopsCannotRetainThePriorEstimate(
    string changed
  )
  {
    using var context = new BunitContext();
    var clock = new FakeTimeProvider(
      new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)
    );
    context.Services.AddSingleton<TimeProvider>(clock);
    var (route, stop, eta) = Inputs(clock);
    var component = context.Render<NextLoadDetailsCard>(p =>
      p.Add(x => x.Route, route).Add(x => x.Stop, stop).Add(x => x.Eta, eta)
    );
    DispatchResponse? details =
      changed == "completed"
        ? new()
        {
          Id = route.Id,
          Stops =
          [
            new() { Id = stop.Id, DeliveredAt = clock.GetUtcNow().UtcDateTime },
          ],
        }
        : null;
    component.Render(p =>
      p.Add(
          x => x.Route,
          changed == "load" ? route with { Id = Guid.NewGuid() } : route
        )
        .Add(
          x => x.Stop,
          changed == "stop" ? stop with { Id = Guid.NewGuid() } : stop
        )
        .Add(x => x.Details, details)
        .Add(x => x.Eta, (DispatchEta?)null)
    );
    Assert.Empty(component.FindAll(".arrival-estimate"));
    Assert.Equal(
      "ETA —",
      component.Find(".fleet-route-popup__eta").TextContent
    );
    Assert.DoesNotContain(
      "Sep 8 · 01:00 PM",
      component.Find(".fleet-route-popup__eta").TextContent
    );
  }

  private static (NextLoadRoute, PlanStop, DispatchEta) Inputs(
    FakeTimeProvider clock
  )
  {
    var id = Guid.NewGuid();
    var stop = new PlanStop(
      Guid.NewGuid(),
      "Warehouse",
      "123 Main Street",
      1,
      new(40, -80)
    );
    var route = new NextLoadRoute(
      id,
      1373,
      "ready",
      [],
      [new(40, -80, "Delivery") { Id = stop.Id }]
    );
    var now = clock.GetUtcNow().UtcDateTime;
    var eta = new DispatchEta(
      now,
      now.AddMinutes(2),
      [
        new(stop.Id, now.AddHours(1), "UTC", null, 0, 60, 0)
        {
          DispatchId = id,
        },
      ],
      null,
      []
    );
    return (route, stop, eta);
  }
}
