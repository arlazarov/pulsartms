using System.Text.Json;
using Application.Caching;
using Application.Features.Auth.Interfaces;
using Application.Interfaces;
using Domain.Entities;
using Infrastructure.Identity;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ApplicationServices = Application.DependencyInjection;
using InfrastructureServices = Infrastructure.DependencyInjection;

namespace Server.Tests.Identity;

[Trait("Category", "Identity")]
public class IdentityTests
{
  [Fact]
  public async Task FiveFailedPasswordsLockTheAccountForFifteenMinutes()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddDataProtection();
    ApplicationServices.AddApplication(services);
    InfrastructureServices.AddInfrastructure(
      services,
      new ConfigurationBuilder().Build()
    );
    services.RemoveAll<DbContextOptions<AppDbContext>>();
    services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
    services.AddDbContext<AppDbContext>(o => o.UseSqlite(connection));
    await using var provider = services.BuildServiceProvider();
    await using var scope = provider.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
    var manager = scope.ServiceProvider.GetRequiredService<
      UserManager<AppUser>
    >();
    var user = new AppUser
    {
      UserName = "locked@example.com",
      Email = "locked@example.com",
    };
    Assert.True((await manager.CreateAsync(user, "password123")).Succeeded);
    db.Users.Add(
      new User
      {
        Id = Guid.NewGuid(),
        IdentityUserId = user.Id,
        Email = user.Email,
        Name = "Test",
        IsActive = true,
      }
    );
    await db.SaveChangesAsync();
    var context = new DefaultHttpContext
    {
      RequestServices = scope.ServiceProvider,
    };
    scope
      .ServiceProvider.GetRequiredService<IHttpContextAccessor>()
      .HttpContext = context;
    var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
    for (var attempt = 0; attempt < 5; attempt++)
      Assert.False(await auth.LoginAsync(user.Email, "wrong-password"));
    Assert.True(await manager.IsLockedOutAsync(user));
    Assert.InRange(
      (user.LockoutEnd!.Value - DateTimeOffset.UtcNow).TotalMinutes,
      14,
      15
    );
    Assert.False(await auth.LoginAsync(user.Email, "password123"));
    Assert.True(
      (
        await manager.SetLockoutEndDateAsync(
          user,
          DateTimeOffset.UtcNow.AddMinutes(-1)
        )
      ).Succeeded
    );
    context.Response.Body = new MemoryStream();
    Assert.True(await auth.LoginAsync(user.Email, "password123"));
  }

  [Fact]
  public async Task DeactivationRevokesExistingPrincipalAndBlocksRefreshAndLogin()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddDataProtection();
    ApplicationServices.AddApplication(services);
    InfrastructureServices.AddInfrastructure(
      services,
      new ConfigurationBuilder().Build()
    );
    services.RemoveAll<DbContextOptions<AppDbContext>>();
    services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
    services.AddDbContext<AppDbContext>(o => o.UseSqlite(connection));
    await using var provider = services.BuildServiceProvider();
    await using var scope = provider.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
    var manager = scope.ServiceProvider.GetRequiredService<
      UserManager<AppUser>
    >();
    var signIn = scope.ServiceProvider.GetRequiredService<
      SignInManager<AppUser>
    >();
    var user = new AppUser
    {
      UserName = "user@example.com",
      Email = "user@example.com",
      LockoutEnabled = false,
    };
    Assert.True((await manager.CreateAsync(user, "password123")).Succeeded);
    Assert.True((await manager.SetLockoutEnabledAsync(user, false)).Succeeded);
    db.Users.Add(
      new User
      {
        Id = Guid.NewGuid(),
        IdentityUserId = user.Id,
        Email = user.Email,
        Name = "Test",
        IsActive = true,
      }
    );
    await db.SaveChangesAsync();
    var principal = await signIn.CreateUserPrincipalAsync(user);
    var context = new DefaultHttpContext
    {
      RequestServices = scope.ServiceProvider,
      User = principal,
    };
    context.Response.Body = new MemoryStream();
    scope
      .ServiceProvider.GetRequiredService<IHttpContextAccessor>()
      .HttpContext = context;
    var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
    Assert.True(await auth.LoginAsync(user.Email, "password123"));
    using var tokens = JsonDocument.Parse(
      ((MemoryStream)context.Response.Body).ToArray()
    );
    var refreshToken = tokens
      .RootElement.GetProperty("refreshToken")
      .GetString()!;
    var reads = scope.ServiceProvider.GetRequiredService<ReadCache>();
    var validCalls = 0;
    var middleware = new SessionValidationMiddleware(_ =>
    {
      validCalls++;
      return Task.CompletedTask;
    });
    await middleware.InvokeAsync(context, signIn, db, reads);
    await middleware.InvokeAsync(context, signIn, db, reads);
    Assert.Equal(2, validCalls);
    var identity = scope.ServiceProvider.GetRequiredService<IIdentityService>();
    Assert.True((await identity.SetActiveAsync(user.Id, false)).Success);
    Assert.True(await manager.IsLockedOutAsync(user));
    Assert.Null(await signIn.ValidateSecurityStampAsync(principal));
    var invoked = false;
    await new SessionValidationMiddleware(_ =>
    {
      invoked = true;
      return Task.CompletedTask;
    }).InvokeAsync(context, signIn, db, reads);
    Assert.False(invoked);
    Assert.Equal(401, context.Response.StatusCode);
    Assert.False(await auth.RefreshAsync(refreshToken));
    Assert.False(await auth.LoginAsync(user.Email, "password123"));
    Assert.True((await identity.SetActiveAsync(user.Id, true)).Success);
    (await db.Users.SingleAsync()).IsActive = false;
    await db.SaveChangesAsync();
    Assert.False(await auth.LoginAsync(user.Email, "password123"));
  }
}
