using Application.Features.Auth.Queries;
using Application.Features.Users.Commands;
using Application.Interfaces;
using Domain.Entities;
using Infrastructure.Identity;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc;

namespace Server.Tests.Identity;

[Trait("Category", "Identity")]
public class CurrentUserTests
{
  [Fact]
  public void ControllersDoNotAccessPersistenceOrClaims()
  {
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AMFTMS.slnx"))) directory = directory.Parent;
    Assert.NotNull(directory);
    foreach (var file in Directory.GetFiles(Path.Combine(directory.FullName, "Server/API/Controllers"), "*.cs"))
    {
      var source = File.ReadAllText(file);
      foreach (var forbidden in new[] { "IAppDbContext", "EntityFrameworkCore", "FindFirstValue", "using Infrastructure.", "IAuthorizationService" })
        Assert.DoesNotContain(forbidden, source);
    }
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task MePreservesExistingWireContract(bool authenticated)
  {
    await using var fixture = await Fixture.Create();
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddMediatR(options => options.RegisterServicesFromAssemblyContaining<GetCurrentUserQuery>());
    services.AddSingleton<IAppDbContext>(fixture.Db);
    services.AddSingleton<ICurrentUser>(new Caller(authenticated, "self"));
    services.AddSingleton<IUserRoleService>(new UserRoleService(fixture.Db));
    using var provider = services.BuildServiceProvider();
    var controller = new API.Controllers.AuthController
    {
      ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { RequestServices = provider } }
    };
    var result = await controller.Me(default);
    if (!authenticated) { Assert.IsType<UnauthorizedResult>(result); return; }
    var response = Assert.IsType<ObjectResult>(result);
    Assert.Equal(200, response.StatusCode);
    Assert.IsType<CurrentUserDto>(response.Value);
  }

  [Fact]
  public void ContextDoesNotTrustAnonymousClaimsOrRetainAnotherRequest()
  {
    var accessor = new HttpContextAccessor();
    var current = new CurrentUser(accessor);
    Assert.False(current.IsAuthenticated);
    Assert.Null(current.IdentityUserId);
    accessor.HttpContext = new DefaultHttpContext
    {
      User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "one")]))
    };
    Assert.Null(current.IdentityUserId);
    accessor.HttpContext.User = Principal("one");
    Assert.Equal("one", current.IdentityUserId);
    accessor.HttpContext = new DefaultHttpContext { User = Principal("two") };
    Assert.Equal("two", current.IdentityUserId);
    accessor.HttpContext = null;
    Assert.Null(current.IdentityUserId);
  }

  [Theory]
  [InlineData(true, "self", true, 200)]
  [InlineData(true, "self", false, 401)]
  [InlineData(true, "missing", true, 401)]
  [InlineData(false, "self", true, 401)]
  [InlineData(true, null, true, 401)]
  public async Task ProfileRequiresAuthenticatedActiveUser(bool authenticated, string? identity, bool active, int status)
  {
    await using var fixture = await Fixture.Create();
    fixture.User.IsActive = active;
    await fixture.Db.SaveChangesAsync();
    var roles = new UserRoleService(fixture.Db);
    var handler = new GetCurrentUserHandler(fixture.Db, new Caller(authenticated, identity), roles);
    var result = await handler.Handle(new(), default);
    Assert.Equal(status, result.StatusCode);
    if (status != 200) { Assert.Null(result.Response); return; }
    Assert.Equal(fixture.User.Id, result.Response!.Id);
    Assert.True(result.Response.IsAdmin);
    await roles.SetAsync("self", "Dispatch");
    Assert.False((await handler.Handle(new(), default)).Response!.IsAdmin);
  }

  [Theory]
  [InlineData("Dispatch", null)]
  [InlineData(null, false)]
  public async Task SelfProtectionRunsInsideUpdateHandlerBeforeAnyIdentityMutation(string? role, bool? active)
  {
    await using var fixture = await Fixture.Create();
    using var reads = TestCache.Create();
    var handler = new UpdateUserHandler(fixture.Db, null!, new UserRoleService(fixture.Db), new Caller(true, "self"), reads);
    var result = await handler.Handle(new(fixture.User.Id, "Changed", "changed@example.com", "password", active, role), default);
    Assert.Equal(400, result.StatusCode);
    Assert.Equal("Test", fixture.User.Name);
    Assert.True(fixture.User.IsActive);
  }

  [Fact]
  public async Task SelfDeletionIsBlockedInsideHandler()
  {
    await using var fixture = await Fixture.Create();
    using var reads = TestCache.Create();
    var result = await new DeleteUserHandler(fixture.Db, null!, new Caller(true, "self"), reads)
      .Handle(new(fixture.User.Id), default);
    Assert.Equal(400, result.StatusCode);
    Assert.True(await fixture.Db.Users.AnyAsync());
  }

  [Fact]
  public async Task EditingOwnNameIsStillAllowed()
  {
    await using var fixture = await Fixture.Create();
    using var reads = TestCache.Create();
    var result = await new UpdateUserHandler(fixture.Db, null!, new UserRoleService(fixture.Db), new Caller(true, "self"), reads)
      .Handle(new(fixture.User.Id, "Updated", null, null, null), default);
    Assert.True(result.Success);
    Assert.Equal("Updated", fixture.User.Name);
  }

  private static ClaimsPrincipal Principal(string id) => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id)], "Test"));
  private sealed record Caller(bool IsAuthenticated, string? IdentityUserId) : ICurrentUser;

  private sealed class Fixture(SqliteConnection connection, AppDbContext db, User user) : IAsyncDisposable
  {
    public AppDbContext Db => db;
    public User User => user;
    public static async Task<Fixture> Create()
    {
      var connection = new SqliteConnection("Data Source=:memory:");
      await connection.OpenAsync();
      var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
      await db.Database.EnsureCreatedAsync();
      db.Set<AppUser>().Add(new AppUser { Id = "self", UserName = "test@example.com" });
      var user = new User { Id = Guid.NewGuid(), IdentityUserId = "self", Name = "Test", Email = "test@example.com", IsActive = true };
      db.Users.Add(user);
      await db.SaveChangesAsync();
      return new(connection, db, user);
    }
    public async ValueTask DisposeAsync() { await db.DisposeAsync(); await connection.DisposeAsync(); }
  }
}
