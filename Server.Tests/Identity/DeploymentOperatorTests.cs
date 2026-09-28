using System.Security.Claims;
using Application.Interfaces;
using Infrastructure.Diagnostics;
using Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Server.Tests.Identity;

// Root's review of dacf2361: releasing sends reaches every carrier, so
// it belongs to the deployment's operators - the identities its
// configuration names - not to any carrier's Admin. And a process the
// platform did not name has no revision to release.
[Trait("Category", "Identity")]
[Trait("Kind", "Unit")]
public sealed class DeploymentOperatorTests
{
  [Theory]
  [InlineData("operator", "Admin", true)]
  [InlineData("carrier-admin", "Admin", false)]
  [InlineData("operator", "Dispatch", false)]
  [InlineData("operator", null, false)]
  public async Task OnlyAnOperatorWhoIsAnAdminPassesTheOperatorPolicy(
    string identity,
    string? role,
    bool allowed
  )
  {
    await using var services = Services(["operator"], identity, role);
    var authorization = services.GetRequiredService<IAuthorizationService>();

    var result = await authorization.AuthorizeAsync(
      Signed(identity),
      "Operator"
    );

    Assert.Equal(allowed, result.Succeeded);
  }

  // A carrier's Admin keeps every Admin endpoint; only the operator one
  // is closed to them.
  [Fact]
  public async Task ACarrierAdminStillPassesTheAdminPolicy()
  {
    await using var services = Services(["operator"], "carrier-admin", "Admin");
    var authorization = services.GetRequiredService<IAuthorizationService>();

    Assert.True(
      (
        await authorization.AuthorizeAsync(Signed("carrier-admin"), "Admin")
      ).Succeeded
    );
    Assert.False(
      (
        await authorization.AuthorizeAsync(Signed("carrier-admin"), "Operator")
      ).Succeeded
    );
  }

  [Fact]
  public async Task NobodyIsAnOperatorWhenNoneIsConfiguredOrSignedOut()
  {
    await using var none = Services([], "operator", "Admin");
    Assert.False(
      (
        await none.GetRequiredService<IAuthorizationService>()
          .AuthorizeAsync(Signed("operator"), "Operator")
      ).Succeeded
    );
    await using var configured = Services(["operator"], "operator", "Admin");
    var signedOut = new ClaimsPrincipal(
      new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "operator")])
    );
    Assert.False(
      (
        await configured
          .GetRequiredService<IAuthorizationService>()
          .AuthorizeAsync(signedOut, "Operator")
      ).Succeeded
    );
  }

  [Theory]
  [InlineData(null, null)]
  [InlineData("", null)]
  [InlineData("Bad_Name", null)]
  [InlineData("-leading", null)]
  [InlineData("trailing-", null)]
  [InlineData("has space", null)]
  [InlineData(
    "a123456789012345678901234567890123456789012345678901234567890123",
    null
  )]
  [InlineData(
    "amftms-api-b-13babfb4-d0f4-4495-a641-d46360b95909",
    "amftms-api-b-13babfb4-d0f4-4495-a641-d46360b95909"
  )]
  public void ARevisionIsNamedOnlyAsThePlatformNamesIt(
    string? configured,
    string? name
  ) =>
    Assert.Equal(
      name,
      new DeploymentRevision(
        new ConfigurationBuilder()
          .AddInMemoryCollection(
            new Dictionary<string, string?> { ["K_REVISION"] = configured }
          )
          .Build()
      ).Name
    );

  private static ServiceProvider Services(
    string[] operators,
    string identity,
    string? role
  ) =>
    new ServiceCollection()
      .AddLogging()
      .AddAuthorization(AuthorizationPolicies.Configure)
      .AddSingleton<IUserRoleService>(new Roles(identity, role))
      .AddSingleton<IDeploymentOperators>(
        new DeploymentOperators(
          new ConfigurationBuilder()
            .AddInMemoryCollection(
              operators.Select(
                (x, i) =>
                  KeyValuePair.Create<string, string?>(
                    $"Operations:Operators:{i}",
                    x
                  )
              )
            )
            .Build()
        )
      )
      .AddScoped<IAuthorizationHandler, AdminAuthorizationHandler>()
      .AddScoped<IAuthorizationHandler, OperatorAuthorizationHandler>()
      .BuildServiceProvider();

  private static ClaimsPrincipal Signed(string identity) =>
    new(
      new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, identity)],
        "Test"
      )
    );

  private sealed class Roles(string identity, string? role) : IUserRoleService
  {
    public Task<string?> GetAsync(string identityId, CancellationToken ct) =>
      Task.FromResult(identityId == identity ? role : null);

    public Task<Dictionary<Guid, string>> GetAsync(
      IReadOnlyCollection<Guid> ids,
      CancellationToken ct
    ) => Task.FromResult(new Dictionary<Guid, string>());

    public Task SetAsync(
      string identityId,
      string role,
      CancellationToken ct
    ) => Task.CompletedTask;
  }
}
