using Bunit;
using Client.Models.DTO.Dispatch;
using Client.Models.DTO.Planning;
using Client.Pages.FleetMap;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Client.Tests.Fleet;

[Trait("Category", "Fleet")]
[Trait("Kind", "Component")]
public sealed class NextLoadAssignmentTests
{
  [Fact]
  public void SelectedStopOverridesLoadAssignmentByIdentityAndUpdatesWhenSelectionChanges()
  {
    using var context = Context();
    var details = Details();
    var selected = details.Stops[1];
    selected.TruckNumber = "11007";
    selected.TrailerNumber = "44120";
    selected.DriverName = "Next driver";
    var component = Render(context, details, selected.Id);

    Assert.Equal(["11007", "44120", "Next driver"], Values(component));
    component.Render(p => p.Add(x => x.Stop, Stop(details.Stops[0].Id)));
    Assert.Equal(["54777", "GG1030", "Load driver"], Values(component));
  }

  [Fact]
  public void EmptyStopFieldsUseMatchingLoadAssignment()
  {
    using var context = Context();
    var details = Details();
    details.Stops[1].TruckNumber = " ";
    var component = Render(context, details, details.Stops[1].Id);
    Assert.Equal(["54777", "GG1030", "Load driver"], Values(component));
  }

  [Fact]
  public void MissingOrMismatchedDetailsCannotLeakAnotherAssignment()
  {
    using var context = Context();
    var details = Details();
    var component = Render(context, details, details.Stops[1].Id);
    component.Render(p =>
      p.Add(
        x => x.Details,
        new DispatchResponse
        {
          Id = Guid.NewGuid(),
          Stops = details.Stops,
          TruckNumber = "Wrong truck",
        }
      )
    );
    Assert.Equal(["—", "—", "—"], Values(component));
    component.Render(p => p.Add(x => x.Details, (DispatchResponse?)null));
    Assert.Equal(["—", "—", "—"], Values(component));
    component.Render(p =>
      p.Add(x => x.Details, details).Add(x => x.Stop, Stop(Guid.NewGuid()))
    );
    Assert.Equal(["—", "—", "—"], Values(component));
  }

  [Fact]
  public void DriverOnlyStopDoesNotInheritLoadEquipment()
  {
    using var context = Context();
    var details = Details();
    details.Stops[1].DriverOnly = true;
    var component = Render(context, details, details.Stops[1].Id);
    Assert.Equal(["—", "—", "Load driver"], Values(component));
  }

  private static BunitContext Context()
  {
    var context = new BunitContext();
    context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider());
    return context;
  }

  [Theory]
  [InlineData("Pick Up", "PU #")]
  [InlineData("Drop Off", "DEL #")]
  public async Task StopReferenceUsesExactIdentityAndCopiesItsValue(
    string job,
    string label
  )
  {
    using var context = Context();
    var details = Details();
    details.Stops[0].StopNo = "Wrong reference";
    details.Stops[1].StopNo = " REF-123 ";
    var selected = Stop(details.Stops[1].Id) with { Job = job };
    string? copied = null;
    var component = Render(context, details, selected.Id);
    component.Render(p =>
      p.Add(x => x.Stop, selected).Add(x => x.OnCopy, value => copied = value)
    );
    Assert.Contains(
      label,
      component.Find(".fleet-route-popup__reference").TextContent
    );
    Assert.Equal(
      "REF-123",
      component.Find("[title='Copy stop reference']").TextContent
    );
    await component.Find("[title='Copy stop reference']").ClickAsync(new());
    Assert.Equal("REF-123", copied);
    component.Render(p =>
      p.Add(
        x => x.Details,
        new DispatchResponse { Id = Guid.NewGuid(), Stops = details.Stops }
      )
    );
    Assert.Empty(component.FindAll("[title='Copy stop reference']"));
    component.Render(p =>
      p.Add(x => x.Details, details)
        .Add(x => x.Stop, selected with { Id = Guid.NewGuid() })
    );
    Assert.Empty(component.FindAll("[title='Copy stop reference']"));
    details.Stops[1].DriverOnly = true;
    component.Render(p => p.Add(x => x.Stop, selected));
    Assert.Empty(component.FindAll("[title='Copy stop reference']"));
  }

  private static DispatchResponse Details() =>
    new()
    {
      Id = Guid.NewGuid(),
      TruckNumber = "54777",
      TrailerNumber = "GG1030",
      DriverName = "Load driver",
      Stops =
      [
        new() { Id = Guid.NewGuid(), Sequence = 1 },
        new() { Id = Guid.NewGuid(), Sequence = 2 },
      ],
    };

  private static PlanStop Stop(Guid id) =>
    new(id, "Warehouse", "Webster, NY", 2, new(43.2, -77.4));

  private static IRenderedComponent<NextLoadDetailsCard> Render(
    BunitContext context,
    DispatchResponse details,
    Guid stopId
  ) =>
    context.Render<NextLoadDetailsCard>(p =>
      p.Add(x => x.Route, new NextLoadRoute(details.Id, 1376, "ready", [], []))
        .Add(x => x.Details, details)
        .Add(x => x.Stop, Stop(stopId))
        .Add(x => x.StopIndex, 0)
    );

  private static string[] Values(
    IRenderedComponent<NextLoadDetailsCard> component
  ) =>
    component
      .FindAll(".fleet-map-next-load-card__assignment dd")
      .Select(element => element.TextContent)
      .ToArray();
}
