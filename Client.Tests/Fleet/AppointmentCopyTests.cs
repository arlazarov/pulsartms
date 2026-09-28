using Bunit;
using Client.Models.DTO.Planning;
using Client.Pages.FleetMap;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Client.Tests.Fleet;

// A stop's Appointment copies the window it shows with the zone it is read
// in, never the ETA (the owner, September 28).
[Trait("Category", "Fleet")]
[Trait("Kind", "Component")]
public sealed class AppointmentCopyTests : IDisposable
{
  private readonly BunitContext _context = new();

  public AppointmentCopyTests() =>
    _context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider());

  public void Dispose() => _context.Dispose();

  private IRenderedComponent<NextLoadDetailsCard> Render(
    PlanStop stop,
    string zone
  )
  {
    var id = Guid.NewGuid();
    var route = new NextLoadRoute(
      id,
      1373,
      "ready",
      [],
      [new(40, -80, "Delivery") { Id = stop.Id }]
    );
    var now = new DateTime(2026, 9, 28, 10, 0, 0, DateTimeKind.Utc);
    var eta = new DispatchEta(
      now,
      now.AddMinutes(2),
      [new(stop.Id, now.AddHours(1), zone, null, 0, 60, 0) { DispatchId = id }],
      null,
      []
    );
    return _context.Render<NextLoadDetailsCard>(p =>
      p.Add(x => x.Route, route).Add(x => x.Stop, stop).Add(x => x.Eta, eta)
    );
  }

  private static PlanStop Stop(bool booked) =>
    new(Guid.NewGuid(), "Warehouse", "123 Main Street", 1, new(40, -80))
    {
      ScheduledDate = booked ? new(2026, 9, 28) : null,
      ScheduledTime = booked ? new(8, 0) : null,
      ScheduledTime2 = booked ? new(13, 0) : null,
    };

  [Fact]
  public void TheStopCardCopiesTheWindowWithItsZone()
  {
    _context
      .JSInterop.SetupVoid("navigator.clipboard.writeText", _ => true)
      .SetVoidResult();
    var component = Render(Stop(booked: true), "America/New_York");
    var copy = component.Find(".fleet-route-popup__appointment .copy-value");
    Assert.Equal("Sep 28 · 08:00 AM – 01:00 PM", copy.TextContent.Trim());
    copy.Click();
    var written = _context.JSInterop.Invocations[
      "navigator.clipboard.writeText"
    ];
    Assert.Equal(
      "Sep 28 · 08:00 AM – 01:00 PM EDT",
      written.Single().Arguments[0]
    );
    component.WaitForAssertion(
      () =>
        Assert.Equal(
          "Copied",
          component.Find(".fleet-route-popup__copied").TextContent
        )
    );
  }

  [Fact]
  public void AStopWithoutABookingHasNothingToCopy()
  {
    var component = Render(Stop(booked: false), "America/New_York");
    Assert.Empty(component.FindAll(".fleet-route-popup__appointment button"));
    Assert.Equal(
      "—",
      component
        .Find(".fleet-route-popup__appointment .fleet-route-popup__value")
        .TextContent.Trim()
    );
  }
}
