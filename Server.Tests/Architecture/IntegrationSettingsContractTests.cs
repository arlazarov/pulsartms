using System.Reflection;
using API.Controllers;
using Application.Features.Integrations.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Server.Tests.Architecture;

[Trait("Category", "Architecture")]
[Trait("Kind", "Architecture")]
public sealed class IntegrationSettingsContractTests
{
  [Fact]
  public void AllIntegrationEndpointsRequireAdminAndUseOnlyMediatr()
  {
    var controller = typeof(IntegrationSettingsController);
    Assert.Equal(
      "api/settings/integrations",
      controller.GetCustomAttribute<RouteAttribute>()!.Template
    );
    Assert.Equal(
      "Admin",
      controller.GetCustomAttribute<AuthorizeAttribute>()!.Policy
    );
    Assert.Null(controller.GetCustomAttribute<AllowAnonymousAttribute>());
    Assert.True(
      controller.GetCustomAttribute<ResponseCacheAttribute>()!.NoStore
    );
    Assert.All(
      controller.GetConstructors(),
      constructor => Assert.Empty(constructor.GetParameters())
    );
    var save = controller.GetMethod(
      nameof(IntegrationSettingsController.Save)
    )!;
    Assert.Equal(
      "{provider}",
      save.GetCustomAttribute<HttpPutAttribute>()!.Template
    );
    Assert.Equal(
      20_480L,
      save.GetCustomAttributesData()
        .Single(attribute =>
          attribute.AttributeType == typeof(RequestSizeLimitAttribute)
        )
        .ConstructorArguments[0]
        .Value
    );
    Assert.All(
      controller.GetMethods(
        BindingFlags.Public | BindingFlags.DeclaredOnly | BindingFlags.Instance
      ),
      method =>
        Assert.Null(method.GetCustomAttribute<AllowAnonymousAttribute>())
    );
  }

  [Fact]
  public void ReadContractHasPresenceAndRevisionButNoCredentialValues()
  {
    Assert.Equal(
      new[]
      {
        "CanRestoreDeployment",
        "Configured",
        "Fields",
        "Provider",
        "Revision",
        "UpdatedAt",
        "UsesSavedSettings",
      },
      typeof(IntegrationConnectionState)
        .GetProperties()
        .Select(property => property.Name)
        .Order()
        .ToArray()
    );
    Assert.Equal(
      new[] { "Configured", "Name" },
      typeof(IntegrationCredentialFieldState)
        .GetProperties()
        .Select(property => property.Name)
        .Order()
        .ToArray()
    );
    Assert.Equal(
      new[] { "torqueai", "samsara", "google-email" },
      IntegrationProviderCatalog.Providers
    );
  }
}
