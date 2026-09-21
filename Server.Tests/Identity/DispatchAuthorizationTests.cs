using System.Reflection;
using System.Security.Claims;
using API.Controllers;
using Application.Interfaces;
using Domain.Entities;
using Infrastructure;
using Infrastructure.Identity;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Server.Tests.Identity;

[Trait("Category", "Identity")]
[Trait("Kind", "Integration")]
public sealed class DispatchAuthorizationTests
{
  [Theory]
  [InlineData(typeof(DispatchController), "SetStopCompletion", "Admin", true)]
  [InlineData(
    typeof(DispatchController),
    "SetStopCompletion",
    "Dispatch",
    true
  )]
  [InlineData(typeof(FleetConfigurationController), "List", "Admin", true)]
  [InlineData(typeof(FleetConfigurationController), "List", "Dispatch", false)]
  [InlineData(typeof(FleetConfigurationController), "Get", "Admin", true)]
  [InlineData(typeof(FleetConfigurationController), "Get", "Dispatch", false)]
  [InlineData(typeof(FleetConfigurationController), "Save", "Admin", true)]
  [InlineData(typeof(FleetConfigurationController), "Save", "Dispatch", false)]
  [InlineData(typeof(CostsController), "ForLoad", "Dispatch", true)]
  [InlineData(typeof(CostsController), "ForLoad", "Admin", true)]
  [InlineData(typeof(CostsController), "SetAttribution", "Dispatch", true)]
  [InlineData(typeof(CostsController), "Record", "Dispatch", true)]
  [InlineData(typeof(MileageController), "SavePolicy", "Admin", true)]
  [InlineData(typeof(MileageController), "SavePolicy", "Dispatch", false)]
  public async Task EndpointsUseCurrentApplicationRoleWithoutRequiringStandardRoleClaims(
    Type controller,
    string action,
    string role,
    bool allowed
  )
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddInfrastructure(new ConfigurationBuilder().Build());
    services.RemoveAll<DbContextOptions<AppDbContext>>();
    services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
    services.AddDbContext<AppDbContext>(o => o.UseSqlite(connection));
    await using var provider = services.BuildServiceProvider();
    await using var scope = provider.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
    var identity = new AppUser
    {
      Id = Guid.NewGuid().ToString(),
      UserName = "operator@example.invalid",
    };
    var user = new User
    {
      Id = Guid.NewGuid(),
      IdentityUserId = identity.Id,
      Name = "Operator",
      Email = identity.UserName,
    };
    db.Set<AppUser>().Add(identity);
    db.Users.Add(user);
    using (
      scope
        .ServiceProvider.GetRequiredService<ICurrentCompany>()
        .As(Company.Amf)
    )
      await db.SaveChangesAsync();
    await scope
      .ServiceProvider.GetRequiredService<IUserRoleService>()
      .SetAsync(identity.Id, role);
    var principal = await scope
      .ServiceProvider.GetRequiredService<SignInManager<AppUser>>()
      .CreateUserPrincipalAsync(identity);
    Assert.False(principal.IsInRole(role));
    var endpoint = controller.GetMethod(action)!;
    var attributes = controller
      .GetCustomAttributes<AuthorizeAttribute>()
      .Concat(endpoint.GetCustomAttributes<AuthorizeAttribute>());
    var policy = await AuthorizationPolicy.CombineAsync(
      scope.ServiceProvider.GetRequiredService<IAuthorizationPolicyProvider>(),
      attributes
    );
    Assert.NotNull(policy);
    var authorization =
      scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
    Assert.Equal(
      allowed,
      (await authorization.AuthorizeAsync(principal, null, policy!)).Succeeded
    );
    Assert.False(
      (
        await authorization.AuthorizeAsync(
          new ClaimsPrincipal(new ClaimsIdentity()),
          null,
          policy!
        )
      ).Succeeded
    );
    var forged = new ClaimsPrincipal(
      new ClaimsIdentity(
        [
          new Claim(ClaimTypes.NameIdentifier, "missing-user"),
          new Claim(ClaimTypes.Role, "Admin"),
        ],
        "fixture"
      )
    );
    Assert.False(
      (await authorization.AuthorizeAsync(forged, null, policy!)).Succeeded
    );
    user.IsActive = false;
    using (
      scope
        .ServiceProvider.GetRequiredService<ICurrentCompany>()
        .As(Company.Amf)
    )
      await db.SaveChangesAsync();
    Assert.False(
      (await authorization.AuthorizeAsync(principal, null, policy!)).Succeeded
    );
  }
}
