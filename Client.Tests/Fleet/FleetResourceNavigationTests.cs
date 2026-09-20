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
}
