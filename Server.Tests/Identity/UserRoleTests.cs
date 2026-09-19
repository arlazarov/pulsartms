using System.Security.Claims;
using Domain.Entities;
using Infrastructure.Identity;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Identity;

[Trait("Category", "Identity")]
[Trait("Kind", "Integration")]
public class UserRoleTests
{
  [Fact]
  public async Task AuthorizationRequiresTheCurrentExplicitRoleEvenWithAnAdminToken()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    var user = await AddUserAsync(db);
    var roles = new UserRoleService(db);
    var principal = new ClaimsPrincipal(
      new ClaimsIdentity(
        [
          new Claim(ClaimTypes.NameIdentifier, user.IdentityUserId),
          new Claim(ClaimTypes.Role, "Admin"),
        ],
        "fixture"
      )
    );
    async Task<bool> Authorized()
    {
      var requirement = new AdminRequirement();
      var context = new AuthorizationHandlerContext(
        [requirement],
        principal,
        null
      );
      await new AdminAuthorizationHandler(roles).HandleAsync(context);
      return context.HasSucceeded;
    }
    Assert.False(await Authorized());
    await roles.SetAsync(user.IdentityUserId, "Admin");
    Assert.True(await Authorized());
    await roles.SetAsync(user.IdentityUserId, "Dispatch");
    Assert.False(await Authorized());
    await roles.SetAsync(user.IdentityUserId, "Admin");
    user.IsActive = false;
    await db.SaveChangesAsync();
    Assert.False(await Authorized());
  }

  [Fact]
  public async Task ConcurrentFirstAssignmentsKeepOneRoleAndWorkingReads()
  {
    var connectionString =
      $"Data Source=roles-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    await using var connection = new SqliteConnection(connectionString);
    await connection.OpenAsync();
    var options = new DbContextOptionsBuilder<AppDbContext>()
      .UseSqlite(connectionString)
      .Options;
    await using var db = new AppDbContext(options);
    await db.Database.EnsureCreatedAsync();
    var user = await AddUserAsync(db);
    var start = new TaskCompletionSource(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    Task Assign(string role) =>
      Task.Run(async () =>
      {
        await start.Task;
        await using var context = new AppDbContext(options);
        await new UserRoleService(context).SetAsync(user.IdentityUserId, role);
      });
    var first = Assign("Admin");
    var second = Assign("Dispatch");
    start.SetResult();
    await Task.WhenAll(first, second);
    Assert.Single(
      await db
        .UserClaims.Where(claim => claim.ClaimType == "amftms:role")
        .ToListAsync()
    );
    var roles = new UserRoleService(db);
    var role = await roles.GetAsync(user.IdentityUserId);
    Assert.Contains(role, new[] { "Admin", "Dispatch" });
    Assert.Equal(role, (await roles.GetAsync(new[] { user.Id }))[user.Id]);
  }

  [Fact]
  public async Task UniqueApplicationRoleDoesNotRestrictOtherMultiValueClaims()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    var user = await AddUserAsync(db);
    db.UserClaims.AddRange(
      new()
      {
        UserId = user.IdentityUserId,
        ClaimType = "department",
        ClaimValue = "one",
      },
      new()
      {
        UserId = user.IdentityUserId,
        ClaimType = "department",
        ClaimValue = "two",
      }
    );
    await db.SaveChangesAsync();
    await new UserRoleService(db).SetAsync(user.IdentityUserId, "Admin");
    db.UserClaims.Add(
      new()
      {
        UserId = user.IdentityUserId,
        ClaimType = "amftms:role",
        ClaimValue = "Dispatch",
      }
    );
    await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    Assert.Equal(
      2,
      await db.UserClaims.CountAsync(claim => claim.ClaimType == "department")
    );
  }

  [Fact]
  public async Task LegacyDuplicateClaimsFailClosedAndExplicitAssignmentRepairsThem()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    var user = await AddUserAsync(db);
    await db.Database.ExecuteSqlRawAsync(
      "DROP INDEX \"UX_AspNetUserClaims_ApplicationRole\""
    );
    db.UserClaims.AddRange(
      new()
      {
        UserId = user.IdentityUserId,
        ClaimType = "amftms:role",
        ClaimValue = "Admin",
      },
      new()
      {
        UserId = user.IdentityUserId,
        ClaimType = "amftms:role",
        ClaimValue = "Admin",
      }
    );
    await db.SaveChangesAsync();
    var roles = new UserRoleService(db);
    Assert.Equal("Dispatch", await roles.GetAsync(user.IdentityUserId));
    Assert.Equal(
      "Dispatch",
      (await roles.GetAsync(new[] { user.Id }))[user.Id]
    );
    await roles.SetAsync(user.IdentityUserId, "Admin");
    Assert.Single(await db.UserClaims.ToListAsync());
    Assert.Equal("Admin", await roles.GetAsync(user.IdentityUserId));
  }

  private static async Task<User> AddUserAsync(AppDbContext db)
  {
    var identity = new AppUser
    {
      Id = Guid.NewGuid().ToString(),
      UserName = "role-test@example.test",
    };
    var user = new User
    {
      Id = Guid.NewGuid(),
      IdentityUserId = identity.Id,
      Name = "Role test",
      Email = "role-test@example.test",
    };
    db.Set<AppUser>().Add(identity);
    db.Users.Add(user);
    await db.SaveChangesAsync();
    return user;
  }

  [Fact]
  public async Task MissingRolesCannotGrantAdministrationAndExplicitRolesPersist()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    var identity = new AppUser
    {
      Id = "legacy",
      UserName = "legacy@example.com",
    };
    var user = new User
    {
      Id = Guid.NewGuid(),
      IdentityUserId = identity.Id,
      Name = "Legacy",
      Email = "legacy@example.com",
    };
    db.Set<AppUser>().Add(identity);
    db.Users.Add(user);
    await db.SaveChangesAsync();
    var roles = new UserRoleService(db);
    Assert.Equal("Dispatch", await roles.GetAsync(identity.Id));
    Assert.Equal(
      "Dispatch",
      (await roles.GetAsync(new[] { user.Id }))[user.Id]
    );
    Assert.Empty(await db.UserClaims.ToListAsync());
    await roles.SetAsync(identity.Id, "Dispatch");
    Assert.Equal("Dispatch", await roles.GetAsync(identity.Id));
    Assert.Equal(
      "Dispatch",
      (await roles.GetAsync(new[] { user.Id }))[user.Id]
    );
    await roles.SetAsync(identity.Id, "Admin");
    Assert.Equal("Admin", await roles.GetAsync(identity.Id));
    Assert.Single(await db.UserClaims.ToListAsync());
    await Assert.ThrowsAsync<ArgumentException>(
      () => roles.SetAsync(identity.Id, "Owner")
    );
    user.IsActive = false;
    await db.SaveChangesAsync();
    Assert.Null(await roles.GetAsync(identity.Id));
    Assert.Null(await roles.GetAsync("unknown"));
  }
}
