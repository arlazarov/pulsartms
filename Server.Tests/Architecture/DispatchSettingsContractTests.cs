using System.Reflection;
using API.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Server.Tests.Architecture;

[Trait("Category", "Architecture")]
[Trait("Kind", "Architecture")]
public sealed class DispatchSettingsContractTests
{
  [Fact]
  public void DispatchersCanReadPrefixButOnlyAdminsCanSaveWithoutChangingFuelSettingsPolicy()
  {
    var controller = typeof(DispatchSettingsController);
    Assert.Equal("api/settings/dispatch", controller.GetCustomAttribute<RouteAttribute>()!.Template);
    Assert.NotNull(controller.GetCustomAttribute<AuthorizeAttribute>());
    Assert.Null(controller.GetCustomAttribute<AllowAnonymousAttribute>());
    Assert.Null(controller.GetCustomAttribute<AuthorizeAttribute>()!.Policy);
    var read = controller.GetMethod(nameof(DispatchSettingsController.Get))!;
    Assert.NotNull(read.GetCustomAttribute<HttpGetAttribute>());
    Assert.Null(read.GetCustomAttribute<AllowAnonymousAttribute>());
    Assert.Null(read.GetCustomAttribute<AuthorizeAttribute>());
    var save = controller.GetMethod(nameof(DispatchSettingsController.Save))!;
    Assert.NotNull(save.GetCustomAttribute<HttpPutAttribute>());
    Assert.Equal("Admin", save.GetCustomAttribute<AuthorizeAttribute>()!.Policy);
    Assert.Null(save.GetCustomAttribute<AllowAnonymousAttribute>());
    Assert.Equal("Admin", typeof(SettingsController).GetCustomAttribute<AuthorizeAttribute>()!.Policy);
    Assert.True(typeof(BaseController).IsAssignableFrom(controller));
    Assert.All(controller.GetConstructors(), constructor => Assert.Empty(constructor.GetParameters()));
  }
}
