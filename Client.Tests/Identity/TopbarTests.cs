using Bunit;
using Client.Layout;
using Client.Tests.Support;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Client.Tests.Identity;

[Trait("Category", "Identity")]
[Trait("Kind", "Component")]
public sealed class TopbarTests
{
  [Theory]
  [InlineData("/fleet/map", "Fleet Map")]
  [InlineData("/dispatch/5a0e5c1e-7d5b-4a61-9d7e-000000000200", "Dispatch")]
  [InlineData("/settings/fleet/trucks", "Fleet")]
  [InlineData("/settings/personal", "Personal settings")]
  public async Task TheBarNamesThePageAndOffersTheSharedAccountMenu(
    string path,
    string page
  )
  {
    await using var context = new ClientComponentContext(
      (_, _) =>
        throw new InvalidOperationException("The bar must not fetch data.")
    );
    context.AddAuthenticationServices();
    context.AddAuthorization().SetAuthorized("Ada Lovelace");
    context.Services.GetRequiredService<NavigationManager>().NavigateTo(path);

    var component = context.Render<Topbar>();

    Assert.Equal(page, component.Find(".topbar__crumbs b").TextContent);
    Assert.Equal(
      "Ada Lovelace",
      component.Find(".topbar__account-text strong").TextContent
    );
    Assert.Empty(component.FindAll("#topbar-account-actions"));
    component.Find(".topbar__account").Click();
    Assert.Contains(
      "Logout",
      component.Find("#topbar-account-actions button").TextContent
    );
  }
}
