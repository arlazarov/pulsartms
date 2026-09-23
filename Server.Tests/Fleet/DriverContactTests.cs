using Application.Features.Fleet.Commands;
using Application.Features.Fleet.Commands.SyncFleet;
using Application.Features.Fleet.Queries;
using Application.Interfaces;
using Application.Models;
using Domain.Entities;
using Domain.Entities.Fleet;
using Domain.Models.Fleet;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Server.Tests.Fleet;

// A driver's phone and email follow the telematics source until somebody
// here sets or clears them; later imports then refresh only the source
// copy. The WhatsApp number is never taken from the ordinary phone.
[Trait("Category", "Fleet")]
[Trait("Kind", "Integration")]
public sealed class DriverContactTests
{
  private const string Dispatcher = "dispatcher-identity";
  private const string Elsewhere = "elsewhere-identity";

  [Fact]
  public async Task ImportFillsContactsUntilTheyAreChangedHere()
  {
    await using var f = await PlanningRefreshFixture.CreateAsync();
    await SyncAsync(f, "5558234327", "driver@example.com");
    var driver = await f.Db.Drivers.SingleAsync();
    Assert.Equal("5558234327", driver.ImportedPhone);
    Assert.Equal("+15558234327", driver.Phone);
    Assert.Equal("driver@example.com", driver.Email);
    Assert.Null(driver.WhatsAppPhone);
    Assert.Equal(0, driver.ContactRevision);

    await SyncAsync(f, "555 823 9999", "new@example.com");
    Assert.Equal("+15558239999", driver.Phone);
    Assert.Equal("new@example.com", driver.Email);
    Assert.Equal(1, driver.ContactRevision);

    // The same source again changes nothing.
    await SyncAsync(f, "555 823 9999", "new@example.com");
    Assert.Equal(1, driver.ContactRevision);
    Assert.Null(driver.WhatsAppPhone);
  }

  [Fact]
  public async Task LocalEditsAndClearsSurviveImportsAndCanReturnToSource()
  {
    await using var f = await PlanningRefreshFixture.CreateAsync();
    await SeedUserAsync(f, "Dispatch");
    await SyncAsync(f, "5558234327", "driver@example.com");
    var id = (await f.Db.Drivers.SingleAsync()).Id;

    var saved = await UpdateAsync(
      f,
      id,
      new(0, "+44 20 7946 0958", false, "", false, "(555) 234-2222")
    );
    Assert.True(saved.Success);
    var state = saved.Response!;
    Assert.Equal("+442079460958", state.Phone.Value);
    Assert.True(state.Phone.IsLocal);
    Assert.Equal("5558234327", state.Phone.Source);
    Assert.Null(state.Email.Value);
    Assert.True(state.Email.IsLocal);
    Assert.Equal("+15552342222", state.WhatsAppPhone);

    await SyncAsync(f, "5552009999", "other@example.com");
    var driver = await f.Db.Drivers.AsNoTracking().SingleAsync();
    Assert.Equal("+442079460958", driver.Phone);
    Assert.Null(driver.Email);
    Assert.Equal("5552009999", driver.ImportedPhone);
    Assert.Equal("+15552342222", driver.WhatsAppPhone);

    var restored = await UpdateAsync(
      f,
      id,
      new(driver.ContactRevision, null, true, null, true, null)
    );
    Assert.True(restored.Success);
    Assert.Equal("+15552009999", restored.Response!.Phone.Value);
    Assert.False(restored.Response.Phone.IsLocal);
    Assert.Equal("other@example.com", restored.Response.Email.Value);
    Assert.Null(restored.Response.WhatsAppPhone);

    await SyncAsync(f, "5557776666", null);
    driver = await f.Db.Drivers.AsNoTracking().SingleAsync();
    Assert.Equal("+15557776666", driver.Phone);
    Assert.Null(driver.Email);
  }

  [Fact]
  public async Task ASourcePhoneWithoutItsCountryIsShownButNotUsable()
  {
    await using var f = await PlanningRefreshFixture.CreateAsync();
    await SeedUserAsync(f, "Admin");
    await SyncAsync(f, "823-4327", null);
    var id = (await f.Db.Drivers.SingleAsync()).Id;
    var state = (await ReadAsync(f, id, "Admin")).Response!;
    Assert.Equal("823-4327", state.Phone.Value);
    Assert.False(state.Phone.Usable);
    Assert.False(state.Phone.IsLocal);
  }

  [Fact]
  public async Task AStaleOrInvalidChangeIsRefused()
  {
    await using var f = await PlanningRefreshFixture.CreateAsync();
    await SeedUserAsync(f, "Dispatch");
    await SyncAsync(f, "5558234327", null);
    var id = (await f.Db.Drivers.SingleAsync()).Id;
    await SyncAsync(f, "5558230000", null);

    var stale = await UpdateAsync(f, id, new(0, "", false, null, true, null));
    Assert.Equal(409, stale.StatusCode);

    var command = new UpdateDriverContactCommand(
      id,
      new(1, "823-4327", false, "not an address", false, "5558234327 x1")
    );
    Assert.Equal(3, command.Wrong().Count());
    // A field taken from the source is not checked against what was typed.
    Assert.Empty(
      new UpdateDriverContactCommand(
        id,
        new(1, "823-4327", true, "not an address", true, null)
      ).Wrong()
    );
  }

  [Fact]
  public async Task OnlyActiveDispatchersOfTheOwningCarrierSeeOrChangeThem()
  {
    await using var f = await PlanningRefreshFixture.CreateAsync();
    await SyncAsync(f, "5558234327", "driver@example.com");
    var id = (await f.Db.Drivers.SingleAsync()).Id;

    // No application user yet, then an inactive one.
    Assert.Equal(403, (await ReadAsync(f, id)).StatusCode);
    var user = await SeedUserAsync(f, "Dispatch");
    user.IsActive = false;
    await f.Db.SaveChangesAsync();
    Assert.Equal(403, (await ReadAsync(f, id)).StatusCode);
    Assert.Equal(
      403,
      (await UpdateAsync(f, id, new(0, "", false, null, true, null))).StatusCode
    );

    // A dispatcher of another carrier finds no such driver.
    var other = Guid.NewGuid();
    await using var scope = f.NewScope();
    var company = scope.ServiceProvider.GetRequiredService<ICurrentCompany>();
    using var serving = company.As(other);
    var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
    db.Users.Add(
      new User
      {
        Id = Guid.NewGuid(),
        IdentityUserId = Elsewhere,
        Name = "Elsewhere",
        Email = "elsewhere@example.invalid",
      }
    );
    await db.SaveChangesAsync(default);
    var roles = new Roles("Dispatch");
    Assert.Equal(
      404,
      (
        await new GetDriverContactHandler(
          db,
          new Caller(Elsewhere),
          roles
        ).Handle(new(id), default)
      ).StatusCode
    );
    Assert.Equal(
      404,
      (
        await new UpdateDriverContactHandler(
          db,
          new Caller(Elsewhere),
          roles,
          f.Time
        ).Handle(new(id, new(0, "", false, null, true, null)), default)
      ).StatusCode
    );
    Assert.Equal(
      "+15558234327",
      (await f.Db.Drivers.AsNoTracking().SingleAsync()).Phone
    );
  }

  private static Task SyncAsync(
    PlanningRefreshFixture f,
    string? phone,
    string? email
  ) =>
    SaveAfter(
      f,
      DriverSync.SyncAsync(
        f.Db,
        [
          new ExternalDriver
          {
            ExternalId = "samsara-driver",
            Name = "Driver One",
            IsActive = true,
            Phone = phone,
            Email = email,
          },
        ]
      )
    );

  private static async Task SaveAfter(PlanningRefreshFixture f, Task work)
  {
    await work;
    await f.Db.SaveChangesAsync();
  }

  private static async Task<User> SeedUserAsync(
    PlanningRefreshFixture f,
    string role
  )
  {
    var user = new User
    {
      Id = Guid.NewGuid(),
      IdentityUserId = Dispatcher,
      Name = "Dispatcher",
      Email = "dispatcher@example.invalid",
    };
    f.Db.Users.Add(user);
    await f.Db.SaveChangesAsync();
    return user;
  }

  private static Task<RequestResponse<DriverContactState>> ReadAsync(
    PlanningRefreshFixture f,
    Guid id,
    string role = "Dispatch"
  ) =>
    new GetDriverContactHandler(f.Db, new Caller(), new Roles(role)).Handle(
      new(id),
      default
    );

  private static Task<RequestResponse<DriverContactState>> UpdateAsync(
    PlanningRefreshFixture f,
    Guid id,
    DriverContactUpdate update
  )
  {
    f.Db.ChangeTracker.Clear();
    return new UpdateDriverContactHandler(
      f.Db,
      new Caller(),
      new Roles("Dispatch"),
      f.Time
    ).Handle(new(id, update), default);
  }

  private sealed class Caller(string identity = Dispatcher) : ICurrentUser
  {
    public bool IsAuthenticated => true;
    public string? IdentityUserId => identity;
  }

  private sealed class Roles(string role) : IUserRoleService
  {
    public Task<string?> GetAsync(string identityId, CancellationToken ct) =>
      Task.FromResult<string?>(role);

    public Task<Dictionary<Guid, string>> GetAsync(
      IReadOnlyCollection<Guid> ids,
      CancellationToken ct
    ) => throw new NotSupportedException();

    public Task SetAsync(
      string identityId,
      string role,
      CancellationToken ct
    ) => throw new NotSupportedException();
  }
}
