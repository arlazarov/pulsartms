using System.Runtime.CompilerServices;
using Application.Caching;
using Application.Features.DriverGroups.Commands;
using Application.Features.DriverGroups.Queries;
using Application.Features.DriverGroups.Services;
using Application.Features.Synchronization.Options;
using Application.Interfaces;
using Domain.Entities;
using Domain.Entities.Execution;
using Domain.Entities.Fleet;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Server.Tests.Fleet;

// Driver groups are each dispatcher's own filter: another dispatcher
// neither sees nor changes them; the one chosen group narrows every page's
// driver lists to its drivers and the trucks they are on; removing a group
// removes no driver and puts its owner back on all drivers.
[Trait("Category", "Fleet")]
[Trait("Kind", "Integration")]
public sealed class DriverGroupTests
{
  [Fact]
  public async Task ADispatchersGroupsAndChoiceAreTheirOwn()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var (west, east, local) = await DriversAsync(f);
    await UsersAsync(f);

    var mine = (
      await Handlers(f, "anna").Handle(Save("West", west, east), default)
    ).Response!;
    var theirs = (
      await Handlers(f, "boris").Handle(Save("West", local), default)
    ).Response!;

    Assert.Equal(
      [mine.Id],
      (await ListAsync(f, "anna")).Groups.Select(x => x.Id)
    );
    Assert.Equal(
      [theirs.Id],
      (await ListAsync(f, "boris")).Groups.Select(x => x.Id)
    );
    // Boris can neither change, remove nor choose Anna's group.
    Assert.Equal(
      404,
      (
        await Handlers(f, "boris")
          .Handle(
            new SaveDriverGroupCommand(mine.Id, "Mine", [local], 1),
            default
          )
      ).StatusCode
    );
    Assert.Equal(
      404,
      (
        await Handlers(f, "boris")
          .Handle(new DeleteDriverGroupCommand(mine.Id), default)
      ).StatusCode
    );
    Assert.Equal(
      404,
      (
        await Handlers(f, "boris")
          .Handle(new SelectDriverGroupCommand(mine.Id), default)
      ).StatusCode
    );

    await Handlers(f, "anna")
      .Handle(new SelectDriverGroupCommand(mine.Id), default);

    var anna = await ScopeAsync(f, "anna");
    Assert.Equal((mine.Id, "West"), (anna.GroupId!.Value, anna.GroupName));
    Assert.True(anna.IncludesDriver(west) && anna.IncludesDriver(east));
    Assert.False(anna.IncludesDriver(local));
    Assert.True((await ScopeAsync(f, "boris")).IsAll);
    Assert.Equal(mine.Id, (await ListAsync(f, "anna")).Selected);
    Assert.Null((await ListAsync(f, "boris")).Selected);
    Assert.Equal(
      2,
      await f.Db.DriverGroupMembers.CountAsync(x => x.GroupId == mine.Id)
    );
  }

  // The trucks a group's drivers are on: the one each is assigned to, and
  // those of their live legs as driver or co-driver - not a finished leg's.
  [Fact]
  public async Task TheScopeReachesTheTrucksItsDriversAreOn()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var (west, east, local) = await DriversAsync(f);
    await UsersAsync(f);
    var assigned = await TruckAsync(f, "101", west);
    var shared = await TruckAsync(f, "102", local);
    var finished = await TruckAsync(f, "103", local);
    await LegAsync(f, shared, local, east, "active");
    await LegAsync(f, finished, local, east, "completed");
    var group = (
      await Handlers(f, "anna").Handle(Save("West", west, east), default)
    ).Response!;
    await Handlers(f, "anna")
      .Handle(new SelectDriverGroupCommand(group.Id), default);

    var scope = await ScopeAsync(f, "anna");

    Assert.True(scope.IncludesTruck(assigned));
    Assert.True(scope.IncludesTruck(shared));
    Assert.False(scope.IncludesTruck(finished));
  }

  // Removing a group removes its membership only: the drivers stay, and
  // whoever had it chosen is back on all drivers.
  [Fact]
  public async Task RemovingAGroupKeepsTheDriversAndFallsBackToAll()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var (west, east, _) = await DriversAsync(f);
    await UsersAsync(f);
    var group = (
      await Handlers(f, "anna").Handle(Save("West", west, east), default)
    ).Response!;
    await Handlers(f, "anna")
      .Handle(new SelectDriverGroupCommand(group.Id), default);

    Assert.True(
      (
        await Handlers(f, "anna")
          .Handle(new DeleteDriverGroupCommand(group.Id), default)
      ).Response
    );

    Assert.True((await ScopeAsync(f, "anna")).IsAll);
    Assert.Null((await ListAsync(f, "anna")).Selected);
    Assert.Equal(3, await f.Db.Drivers.CountAsync());
    Assert.Empty(await f.Db.DriverGroupMembers.ToListAsync());
  }

  // Two windows edit the same group at one revision: the second is
  // refused and changes nothing, members included.
  [Fact]
  public async Task AnEditAtAnOldRevisionChangesNothing()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var (west, east, local) = await DriversAsync(f);
    await UsersAsync(f);
    var group = (
      await Handlers(f, "anna").Handle(Save("West", west), default)
    ).Response!;

    var first = await Handlers(f, "anna")
      .Handle(
        new SaveDriverGroupCommand(group.Id, "West", [west, east], 1),
        default
      );
    var second = await Handlers(f, "anna")
      .Handle(
        new SaveDriverGroupCommand(group.Id, "Local", [local], 1),
        default
      );

    Assert.True(first.Success);
    Assert.Equal(409, second.StatusCode);
    var now = Assert.Single((await ListAsync(f, "anna")).Groups);
    Assert.Equal("West", now.Name);
    Assert.Equal(new[] { west, east }.Order(), now.Drivers.Order());
  }

  [Fact]
  public async Task AGroupIsNamedOnceAndHoldsOnlyTheCompanysDrivers()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var (west, _, _) = await DriversAsync(f);
    await UsersAsync(f);
    await Handlers(f, "anna").Handle(Save("West", west), default);

    Assert.Equal(
      409,
      (
        await Handlers(f, "anna").Handle(Save(" West ", west), default)
      ).StatusCode
    );
    Assert.Equal(
      400,
      (await Handlers(f, "anna").Handle(Save("  ", west), default)).StatusCode
    );
    Assert.Equal(
      400,
      (
        await Handlers(f, "anna").Handle(Save("East", Guid.NewGuid()), default)
      ).StatusCode
    );
  }

  // A dispatcher on All reads nothing once the choice is known; one with a
  // group reads only the trucks its drivers are on. Every change to the
  // choice or the group is seen by the next read.
  [Fact]
  public async Task TheChoiceIsReadOnceAndEveryChangeIsSeenAtOnce()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var (west, east, local) = await DriversAsync(f);
    await UsersAsync(f);
    Assert.True((await ScopeAsync(f, "anna")).IsAll);
    f.Counter.Reset();

    Assert.True((await ScopeAsync(f, "anna")).IsAll);
    Assert.Equal(0, f.Counter.Reads);

    var group = (
      await Handlers(f, "anna").Handle(Save("West", west), default)
    ).Response!;
    await Handlers(f, "anna")
      .Handle(new SelectDriverGroupCommand(group.Id), default);
    Assert.Equal([west], (await ScopeAsync(f, "anna")).Drivers);
    f.Counter.Reset();
    Assert.Equal([west], (await ScopeAsync(f, "anna")).Drivers);
    Assert.Equal(1, f.Counter.Reads);

    await Handlers(f, "anna")
      .Handle(
        new SaveDriverGroupCommand(group.Id, "West", [west, east], 1),
        default
      );
    Assert.Equal(
      new[] { west, east }.Order(),
      (await ScopeAsync(f, "anna")).Drivers.Order()
    );
    Assert.False((await ScopeAsync(f, "anna")).IncludesDriver(local));

    await Handlers(f, "anna")
      .Handle(new SelectDriverGroupCommand(null), default);
    Assert.True((await ScopeAsync(f, "anna")).IsAll);
    await Handlers(f, "anna")
      .Handle(new SelectDriverGroupCommand(group.Id), default);
    Assert.False((await ScopeAsync(f, "anna")).IsAll);
    await Handlers(f, "anna")
      .Handle(new DeleteDriverGroupCommand(group.Id), default);
    Assert.True((await ScopeAsync(f, "anna")).IsAll);
  }

  private static SaveDriverGroupCommand Save(
    string name,
    params Guid[] drivers
  ) => new(null, name, drivers, 0);

  private static DriverGroupHandlers Handlers(
    DispatchSyncFixture f,
    string identity
  )
  {
    f.Db.ChangeTracker.Clear();
    return new(f.Db, new Caller(identity), Reads(f), TimeProvider.System);
  }

  private static async Task<DriverGroupsView> ListAsync(
    DispatchSyncFixture f,
    string identity
  )
  {
    f.Db.ChangeTracker.Clear();
    return (
      await new GetDriverGroupsHandler(f.Db, new Caller(identity)).Handle(
        new(),
        default
      )
    ).Response!;
  }

  private static Task<DriverScope> ScopeAsync(
    DispatchSyncFixture f,
    string identity
  )
  {
    f.Db.ChangeTracker.Clear();
    return new DriverScopeReader(
      f.Db,
      new Caller(identity),
      Reads(f)
    ).CurrentAsync(default);
  }

  // One read cache per fixture, shared by the commands and the scope reader
  // as they share one in a request; never across tests, where the same
  // names would find each other's entries.
  private static readonly ConditionalWeakTable<
    DispatchSyncFixture,
    ReadCache
  > Caches = new();

  private static ReadCache Reads(DispatchSyncFixture f) =>
    Caches.GetValue(
      f,
      _ => new ReadCache(Options.Create(new SynchronizationOptions()))
    );

  private static async Task<(Guid, Guid, Guid)> DriversAsync(
    DispatchSyncFixture f
  )
  {
    var drivers = new[] { "West One", "East One", "Local One" }
      .Select(name => new Driver
      {
        Id = Guid.NewGuid(),
        ExternalId = name,
        Name = name,
        IsActive = true,
      })
      .ToArray();
    f.Db.Drivers.AddRange(drivers);
    await f.Db.SaveChangesAsync();
    return (drivers[0].Id, drivers[1].Id, drivers[2].Id);
  }

  private static async Task UsersAsync(DispatchSyncFixture f)
  {
    foreach (var name in new[] { "anna", "boris" })
      f.Db.Users.Add(
        new User
        {
          Id = Guid.NewGuid(),
          IdentityUserId = name,
          Name = name,
          Email = $"{name}@example.invalid",
        }
      );
    await f.Db.SaveChangesAsync();
  }

  private static async Task<Guid> TruckAsync(
    DispatchSyncFixture f,
    string number,
    Guid driver
  )
  {
    var truck = new Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = number,
      UnitNumber = number,
      IsActive = true,
      DriverId = number == "101" ? driver : null,
    };
    f.Db.Trucks.Add(truck);
    await f.Db.SaveChangesAsync();
    return truck.Id;
  }

  private static async Task LegAsync(
    DispatchSyncFixture f,
    Guid truck,
    Guid driver,
    Guid coDriver,
    string status
  )
  {
    f.Db.ExecutionLegs.Add(
      new ExecutionLeg
      {
        Id = Guid.NewGuid(),
        Trip = new() { Id = Guid.NewGuid() },
        TruckId = truck,
        DriverId = driver,
        CoDriverId = coDriver,
        Status = status,
        Revision = 1,
      }
    );
    await f.Db.SaveChangesAsync();
  }

  private sealed class Caller(string identity) : ICurrentUser
  {
    public bool IsAuthenticated => true;
    public string? IdentityUserId => identity;
  }
}
