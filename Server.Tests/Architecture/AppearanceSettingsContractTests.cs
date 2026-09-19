using System.Reflection;
using API.Controllers;
using Application.Features.Users.Commands;
using Application.Features.Users.Models;
using Application.Features.Users.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Server.Tests.Architecture;

[Trait("Category", "Architecture")]
[Trait("Kind", "Architecture")]
public sealed class AppearanceSettingsContractTests
{
  [Fact]
  public void AppearanceRequiresAuthenticationAndAcceptsNoTargetUser()
  {
    var controller = typeof(AppearanceSettingsController);
    var auth = controller.GetCustomAttribute<AuthorizeAttribute>();
    Assert.NotNull(auth);
    Assert.Null(auth.Policy);
    Assert.Null(auth.Roles);
    Assert.Null(controller.GetCustomAttribute<AllowAnonymousAttribute>());
    Assert.Equal(
      "api/settings/appearance",
      controller.GetCustomAttribute<RouteAttribute>()!.Template
    );
    foreach (var name in new[] { "Get", "Save" })
    {
      var action = controller.GetMethod(name)!;
      Assert.Null(action.GetCustomAttribute<AllowAnonymousAttribute>());
      Assert.Null(action.GetCustomAttribute<AuthorizeAttribute>());
      Assert.DoesNotContain(
        action.GetParameters(),
        x => x.ParameterType == typeof(Guid)
      );
    }
    Assert.Equal(
      ["Theme", "TemperatureUnit", "DistanceUnit"],
      typeof(AppearanceSettings).GetProperties().Select(x => x.Name)
    );
    Assert.Equal(
      ["Theme", "TemperatureUnit", "DistanceUnit"],
      typeof(UpdateAppearanceSettingsCommand)
        .GetProperties()
        .Select(x => x.Name)
    );
    Assert.Empty(typeof(GetAppearanceSettingsQuery).GetProperties());
    Assert.Equal(
      "Admin",
      typeof(SettingsController)
        .GetCustomAttribute<AuthorizeAttribute>()!
        .Policy
    );
  }
}
