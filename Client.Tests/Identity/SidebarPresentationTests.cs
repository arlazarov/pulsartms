using System.Net;
using Bunit;
using Client.Layout;
using Client.Services;
using Client.Shared.Brand.BrandLogo;
using Client.Tests.Support;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Client.Tests.Identity;

[Trait("Category", "Identity")]
[Trait("Kind", "Component")]
public sealed class SidebarPresentationTests
{
  [Fact]
  public async Task SidebarShowsTheProductBrandWithoutReplacingAccountIdentity()
  {
    await using var context = new ClientComponentContext(
      (_, _) =>
        throw new InvalidOperationException(
          "The sidebar must not fetch a profile."
        )
    );
    context.AddAuthenticationServices();
    context.AddAuthorization().SetAuthorized("Dispatcher");
    var component = context.Render<Sidebar>();

    var logo = component.FindComponent<BrandLogo>();
    Assert.True(logo.Instance.Reversed);
    Assert.False(logo.Instance.Prominent);
    Assert.Equal(
      "PulsR TMS",
      component
        .Find(".sidebar__brand svg[role='img']")
        .GetAttribute("aria-label")
    );
    Assert.Empty(component.FindAll(".sidebar__monogram, .sidebar__wordmark"));
    Assert.Equal(
      "Dispatcher",
      component.Find(".sidebar__account-text strong").TextContent
    );
  }

  [Fact]
  public async Task AccountDisclosureRetainsTheExistingLogoutFlow()
  {
    var calls = 0;
    await using var context = new ClientComponentContext(
      (request, _) =>
      {
        Assert.Equal("/api/auth/logout", request.RequestUri!.AbsolutePath);
        Assert.Equal(HttpMethod.Post, request.Method);
        calls++;
        return Task.FromResult(
          new HttpResponseMessage(HttpStatusCode.NoContent)
        );
      }
    );
    context.AddAuthenticationServices();
    context.AddAuthorization().SetAuthorized("Dispatcher");
    var storage = context.Services.GetRequiredService<TokenStorageService>();
    await storage.SetTokensAsync("access", "refresh");
    var component = context.Render<Sidebar>();
    component.Find(".sidebar__account").Click();
    await component.Find("#sidebar-account-actions button").ClickAsync(new());
    Assert.Equal(1, calls);
    Assert.Null(await storage.GetAccessTokenAsync());
    Assert.EndsWith(
      "/login",
      context.Services.GetRequiredService<NavigationManager>().Uri
    );
  }

  [Theory]
  [InlineData("Ada Lovelace", "Admin", "AL", "Administrator", true)]
  [InlineData("李", "Dispatch", "李", "Dispatch", false)]
  public async Task AccountUsesExistingClaimsAndKeepsRoleRestrictedNavigation(
    string name,
    string role,
    string initials,
    string label,
    bool admin
  )
  {
    await using var context = new ClientComponentContext(
      (_, _) =>
        throw new InvalidOperationException(
          "The sidebar must not fetch a profile."
        )
    );
    context.AddAuthenticationServices();
    var authorization = context.AddAuthorization();
    authorization.SetAuthorized(name);
    authorization.SetRoles(role);
    var component = context.Render<Sidebar>();

    Assert.Equal(
      name,
      component.Find(".sidebar__account-text strong").TextContent
    );
    Assert.Equal(
      label,
      component.Find(".sidebar__account-text span").TextContent
    );
    Assert.Equal(initials, component.Find(".sidebar__avatar").TextContent);
    Assert.Equal(admin ? 1 : 0, component.FindAll("a[href='/users']").Count);
    Assert.Equal(admin ? 1 : 0, component.FindAll("a[href='/settings']").Count);
    Assert.Single(component.FindAll("a[href='/customers']"));
    Assert.Equal(
      admin ? 1 : 0,
      component.FindAll("a[href='/settings/fleet']").Count
    );
    foreach (var kind in new[] { "trucks", "trailers", "drivers" })
      Assert.Empty(component.FindAll($"a[href='/settings/fleet/{kind}']"));
    Assert.Empty(component.FindAll("#sidebar-account-actions"));
    Assert.Equal(
      "false",
      component.Find(".sidebar__account").GetAttribute("aria-expanded")
    );
    component.Find(".sidebar__account").Click();
    Assert.Equal(
      "true",
      component.Find(".sidebar__account").GetAttribute("aria-expanded")
    );
    Assert.Contains(
      "Logout",
      component.Find("#sidebar-account-actions button").TextContent
    );
    Assert.Equal(
      "Personal settings",
      component.Find("a[href='/settings/personal']").TextContent.Trim()
    );
    component.Find(".sidebar__account").Click();
    Assert.Empty(component.FindAll("#sidebar-account-actions"));
  }

  [Theory]
  [InlineData("/settings/fleet")]
  [InlineData("/settings/fleet/trucks")]
  [InlineData("/settings/fleet/trailers")]
  [InlineData("/settings/fleet/drivers")]
  public async Task FleetUsesOneActiveEntryAcrossResourceTabs(string path)
  {
    await using var context = new ClientComponentContext(
      (_, _) =>
        throw new InvalidOperationException("Navigation must not fetch data.")
    );
    context.AddAuthenticationServices();
    var authorization = context.AddAuthorization();
    authorization.SetAuthorized("Administrator");
    authorization.SetRoles("Admin");
    var component = context.Render<Sidebar>();
    await component.InvokeAsync(
      () =>
        context
          .Services.GetRequiredService<NavigationManager>()
          .NavigateTo(path)
    );
    var active = Assert.Single(component.FindAll(".sidebar__nav a.active"));
    Assert.Equal("/settings/fleet", active.GetAttribute("href"));
    Assert.Equal("Fleet", active.TextContent.Trim());
    Assert.Single(component.FindAll("a[href='/fleet/map']"));
  }
}
