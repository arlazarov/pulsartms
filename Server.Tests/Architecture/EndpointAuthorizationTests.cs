using System.Reflection;
using API.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Server.Tests.Support;

namespace Server.Tests.Architecture;

// The audit of September 27 classified every endpoint by who may call it
// in a read-only review. This makes that table executable: each controller
// action's effective rule - anonymous, a named policy, or any signed-in
// user (the fallback policy or a bare [Authorize]) - is read from its
// attributes and compared with the pinned inventory beside this file. A
// new or changed endpoint fails here until its rule is written down on
// purpose. Tenant scope is not in this table: every tenant table is
// filtered by company (CompanyOwnershipTests, CompanyIsolationTests).
// The four minimal-API mappings in Program.cs (health, OpenAPI) are not
// controllers and are not listed.
[Trait("Category", "Architecture")]
[Trait("Kind", "Architecture")]
public sealed class EndpointAuthorizationTests
{
  private const string Inventory =
    "Server.Tests/Architecture/EndpointAuthorization.txt";

  [Fact]
  public void EveryEndpointHasTheRuleItsInventoryWritesDown()
  {
    var actual = Endpoints().ToArray();
    var pinned = File.ReadAllLines(
        Path.Combine(RepositoryFiles.Root(), Inventory)
      )
      .Where(x => x.Length > 0 && !x.StartsWith('#'))
      .ToArray();

    var added = actual.Except(pinned).ToArray();
    var removed = pinned.Except(actual).ToArray();
    Assert.True(
      added.Length == 0 && removed.Length == 0,
      "Endpoint rules differ from "
        + Inventory
        + ".\nNot pinned:\n"
        + string.Join('\n', added)
        + "\nPinned but gone:\n"
        + string.Join('\n', removed)
    );
  }

  // Anonymous is the exception: sign-in, refresh and the three provider
  // callbacks, nothing else.
  [Fact]
  public void OnlyTheKnownEndpointsAreAnonymous()
  {
    Assert.Equal(
      [
        "Auth.Login POST api/auth/login",
        "Auth.Refresh POST api/auth/refresh",
        "Fuel.GmailNotifications POST api/fuel/gmail-notifications",
        "Storage.Callback GET api/storage/connections/callback",
        "WhatsAppWebhook.Receive POST api/webhooks/whatsapp/{companyKey}",
        "WhatsAppWebhook.Verify GET api/webhooks/whatsapp/{companyKey}",
      ],
      Endpoints()
        .Where(x => x.EndsWith(" -> anonymous", StringComparison.Ordinal))
        .Select(x => x[..x.IndexOf(" -> ", StringComparison.Ordinal)])
        .Order(StringComparer.Ordinal)
    );
  }

  private static IEnumerable<string> Endpoints()
  {
    var controllers = typeof(AuthController)
      .Assembly.GetTypes()
      .Where(x => typeof(ControllerBase).IsAssignableFrom(x) && !x.IsAbstract);
    foreach (var controller in controllers)
    {
      var prefix = controller
        .GetCustomAttribute<RouteAttribute>()
        ?.Template.Replace(
          "[controller]",
          Name(controller).ToLowerInvariant(),
          StringComparison.Ordinal
        );
      foreach (
        var action in controller.GetMethods(
          BindingFlags.Public
            | BindingFlags.Instance
            | BindingFlags.DeclaredOnly
        )
      )
      foreach (var verb in action.GetCustomAttributes<HttpMethodAttribute>())
      {
        var template = verb.Template;
        var route =
          template is null ? prefix
          : template.StartsWith('/') ? template.TrimStart('/')
          : prefix is null ? template
          : $"{prefix}/{template}";
        foreach (var method in verb.HttpMethods)
          yield return $"{Name(controller)}.{action.Name} {method} {route}"
            + $" -> {Rule(controller, action)}";
      }
    }
  }

  private static string Name(Type controller) =>
    controller.Name.EndsWith("Controller", StringComparison.Ordinal)
      ? controller.Name[..^"Controller".Length]
      : controller.Name;

  // AllowAnonymous anywhere on the action's path admits anyone; otherwise
  // every named policy on the controller and the action must pass, and
  // without one the fallback policy (any signed-in user) applies.
  private static string Rule(Type controller, MethodInfo action)
  {
    if (
      action.GetCustomAttribute<AllowAnonymousAttribute>() is not null
      || controller.GetCustomAttribute<AllowAnonymousAttribute>() is not null
    )
      return "anonymous";
    var policies = controller
      .GetCustomAttributes<AuthorizeAttribute>()
      .Concat(action.GetCustomAttributes<AuthorizeAttribute>())
      .SelectMany(x =>
        new[] { x.Policy, x.Roles is null ? null : $"roles:{x.Roles}" }
      )
      .OfType<string>()
      .Distinct()
      .Order(StringComparer.Ordinal)
      .ToArray();
    return policies.Length == 0 ? "signed-in" : string.Join('+', policies);
  }
}
