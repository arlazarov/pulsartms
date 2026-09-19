using Bunit;
using Client.Models.DTO.Dispatch;
using Client.Models.DTO.Dispatch.Workspace;
using Client.Pages.Dispatch;

namespace Client.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Component")]
public sealed class DispatchStopSummaryTests
{
  [Fact]
  public void ProtectedStopKeepsReferencesCargoAndInstructionsWithoutAForm()
  {
    using var context = new BunitContext();
    var stop = new DispatchWorkspaceStop
    {
      Id = Guid.NewGuid(),
      Job = "Delivery",
      Address = "12 Delivery Road",
      City = "Albany",
      Province = "NY",
      StopNo = "DEL-42",
      AppointmentReference = "APPT-84",
      AppointmentMode = "window",
      ScheduledDate = new(2026, 9, 15),
      ScheduledTime = new(9, 0),
      ScheduledDate2 = new(2026, 9, 16),
      ScheduledTime2 = new(20, 0),
      TimeZoneId = "America/New_York",
      ContactName = "Receiving office",
      ContactPhone = "+1 555 010 2222",
      Commodity = "Bottled water",
      Pallets = 12,
      Notes = "Call receiving before breaking the seal.",
    };
    var component = context.Render<DispatchStopSummary>(p =>
      p.Add(x => x.Stop, stop)
    );

    Assert.Contains("12 Delivery Road, Albany, NY", component.Markup);
    Assert.Contains("DEL #", component.Markup);
    Assert.Contains("DEL-42", component.Markup);
    Assert.Contains("Appt #", component.Markup);
    Assert.Contains("APPT-84", component.Markup);
    Assert.Contains("Sep 15", component.Markup);
    Assert.Contains("Sep 16", component.Markup);
    Assert.Contains("09:00 AM", component.Markup);
    Assert.Contains("08:00 PM", component.Markup);
    Assert.Contains("America/New_York", component.Markup);
    Assert.Contains("Receiving office", component.Markup);
    Assert.Contains("Bottled water", component.Markup);
    Assert.Contains(stop.Notes, component.Markup);
    Assert.Empty(component.FindAll("input, select, textarea, button"));
    Assert.All(component.FindAll("h3, dt"), heading =>
      Assert.NotNull(heading.QuerySelector("svg[aria-hidden='true']")));
  }

  [Fact]
  public void ActualEventsBelongOnlyToTheExactSavedStop()
  {
    using var context = new BunitContext();
    var stop = new DispatchWorkspaceStop { Id = Guid.NewGuid() };
    var recorded = new DispatchStopResponse
    {
      Id = Guid.NewGuid(),
      DeliveredAt = new(2026, 9, 16, 15, 0, 0, DateTimeKind.Utc),
    };
    var component = context.Render<DispatchStopSummary>(p =>
      p.Add(x => x.Stop, stop).Add(x => x.Recorded, recorded)
    );

    Assert.DoesNotContain("Delivered", component.Markup);
    recorded.Id = stop.Id;
    component.Render();
    Assert.Contains("Delivered", component.Markup);
  }
}
