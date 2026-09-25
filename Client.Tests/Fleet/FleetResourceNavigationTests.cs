using Bunit;
using Client.Models.DTO.Fleet;
using Client.Pages.Settings;
using Client.Tests.Support;

namespace Client.Tests.Fleet;

[Trait("Category", "Fleet")]
[Trait("Kind", "Component")]
public sealed class FleetResourceNavigationTests
{
  [Fact]
  public void SettingsDoesNotRepeatFleetResourceNavigation()
  {
    using var context = new BunitContext();
    var authorization = context.AddAuthorization();
    authorization.SetAuthorized("Administrator");
    authorization.SetRoles("Admin");
    context.ComponentFactories.AddStub<FleetSettings>();
    context.ComponentFactories.AddStub<StorageSettings>();
    context.ComponentFactories.AddStub<StorageLayoutSettings>();

    var component = context.Render<Client.Pages.Settings.Settings>();

    Assert.Equal("Settings", component.Find("h1").TextContent.Trim());
    Assert.Empty(component.FindAll("nav[aria-label='Fleet configuration']"));
    Assert.Empty(component.FindAll("a[href^='/settings/fleet/']"));
    Assert.Single(
      component.FindComponents<Bunit.TestDoubles.Stub<FleetSettings>>()
    );
  }

  [Fact]
  public async Task FleetRootRestoresTrucksAfterAnotherResourceTab()
  {
    var paths = new List<string>();
    await using var context = new ClientComponentContext(
      (request, _) =>
      {
        Assert.Equal(HttpMethod.Get, request.Method);
        paths.Add(request.RequestUri!.AbsolutePath);
        return Task.FromResult(
          MileageComponentResponses.Ok(new FleetConfigurationList(0, []))
        );
      }
    );
    var component = context.Render<FleetResources>(parameters =>
      parameters.Add(page => page.Kind, "drivers")
    );
    component.Render(parameters => parameters.Add(page => page.Kind, null!));
    Assert.Equal(
      ["/api/settings/fleet/drivers", "/api/settings/fleet/trucks"],
      paths
    );
    Assert.Equal("Trucks", component.Find("h1").TextContent.Trim());
    Assert.Equal(
      ["Trucks", "Trailers", "Drivers"],
      component
        .FindAll("nav[aria-label='Fleet configuration'] a")
        .Select(link => link.TextContent.Trim())
    );
    Assert.Equal(
      "page",
      component
        .Find("a[href='/settings/fleet/trucks']")
        .GetAttribute("aria-current")
    );
  }

  // Active by default, with how many are inactive said beside it; Inactive
  // and All are one press away, read by the server from page 1, and each
  // empty list says which status it is.
  [Fact]
  public async Task ActiveIsTheDefaultAndInactiveIsOnePressAway()
  {
    var queries = new List<string>();
    await using var context = new ClientComponentContext(
      (request, _) =>
      {
        var query = request.RequestUri!.Query;
        queries.Add(query);
        var inactive = query.Contains("status=inactive");
        return Task.FromResult(
          MileageComponentResponses.Ok(
            new FleetConfigurationList(
              inactive ? 0 : 2,
              inactive
                ? []
                :
                [
                  new(Guid.NewGuid(), "11005", "VIN1", true, false, 1),
                  new(Guid.NewGuid(), "11006", "VIN2", true, false, 1),
                ],
              2,
              3
            )
          )
        );
      }
    );
    var component = context.Render<FleetResources>(parameters =>
      parameters.Add(page => page.Kind, "trucks")
    );

    component.WaitForAssertion(
      () => Assert.Equal(2, component.FindAll(".data-table__row").Count)
    );
    Assert.Contains("status=active", Assert.Single(queries));
    Assert.Equal(
      ["Active · 2", "Inactive · 3", "All · 5"],
      component
        .FindAll("[role=group][aria-label='Trucks status'] button")
        .Select(button => button.TextContent.Trim())
    );
    Assert.Equal(
      "true",
      component.Find("#fleet-status-active").GetAttribute("aria-pressed")
    );

    await component.Find("#fleet-status-inactive").ClickAsync(new());

    component.WaitForAssertion(
      () => Assert.Contains("No inactive trucks match.", component.Markup)
    );
    Assert.Contains("status=inactive", queries[^1]);
    Assert.Contains("page=1", queries[^1]);
    Assert.Equal(
      "true",
      component.Find("#fleet-status-inactive").GetAttribute("aria-pressed")
    );
  }
}
