using Bunit;
using Client.Layout;
using Client.Tests.Support;
using Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using System.Net;

namespace Client.Tests.Identity;

[Trait("Category", "Identity")]
[Trait("Kind", "Component")]
public sealed class SidebarPresentationTests
{
    [Fact]
    public async Task AccountDisclosureRetainsTheExistingLogoutFlow()
    {
        var calls = 0;
        await using var context = new ClientComponentContext((request, _) =>
        {
            Assert.Equal("/api/auth/logout", request.RequestUri!.AbsolutePath);
            Assert.Equal(HttpMethod.Post, request.Method);
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        });
        context.AddAuthenticationServices();
        context.AddAuthorization().SetAuthorized("Dispatcher");
        var storage = context.Services.GetRequiredService<TokenStorageService>();
        await storage.SetTokensAsync("access", "refresh");
        var component = context.Render<Sidebar>();
        component.Find(".sidebar__account").Click();
        await component.Find("#sidebar-account-actions button").ClickAsync(new());
        Assert.Equal(1, calls);
        Assert.Null(await storage.GetAccessTokenAsync());
        Assert.EndsWith("/login", context.Services.GetRequiredService<NavigationManager>().Uri);
    }

    [Theory]
    [InlineData("Ada Lovelace", "Admin", "AL", "Administrator", true)]
    [InlineData("李", "Dispatch", "李", "Dispatch", false)]
    public async Task AccountUsesExistingClaimsAndKeepsRoleRestrictedNavigation(
        string name, string role, string initials, string label, bool admin)
    {
        await using var context = new ClientComponentContext((_, _) =>
            throw new InvalidOperationException("The sidebar must not fetch a profile."));
        context.AddAuthenticationServices();
        var authorization = context.AddAuthorization();
        authorization.SetAuthorized(name);
        authorization.SetRoles(role);
        var component = context.Render<Sidebar>();

        Assert.Equal(name, component.Find(".sidebar__account-text strong").TextContent);
        Assert.Equal(label, component.Find(".sidebar__account-text span").TextContent);
        Assert.Equal(initials, component.Find(".sidebar__avatar").TextContent);
        Assert.Equal(admin ? 1 : 0, component.FindAll("a[href='/users']").Count);
        Assert.Equal(admin ? 1 : 0, component.FindAll("a[href='/settings']").Count);
        Assert.Empty(component.FindAll("#sidebar-account-actions"));
        Assert.Equal("false", component.Find(".sidebar__account").GetAttribute("aria-expanded"));
        component.Find(".sidebar__account").Click();
        Assert.Equal("true", component.Find(".sidebar__account").GetAttribute("aria-expanded"));
        Assert.Contains("Logout", component.Find("#sidebar-account-actions button").TextContent);
        component.Find(".sidebar__account").Click();
        Assert.Empty(component.FindAll("#sidebar-account-actions"));
    }
}
