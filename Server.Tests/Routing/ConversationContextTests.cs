using Application.Features.Routing.Commands;
using Application.Features.Routing.Queries;
using Application.Reference;
using Domain.Entities;
using Domain.Entities.Execution;
using Domain.Entities.Fleet;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Routing;

// Beside a conversation: the driver, linked by a dispatcher when the number
// matched nobody, and the truck they are driving - from their planned and
// active execution legs as driver or co-driver, else from the fleet's
// assignment. More than one truck is shown as such, with no loads offered.
[Trait("Category", "Routing")]
[Trait("Kind", "Integration")]
public sealed class ConversationContextTests
{
  [Fact]
  public async Task ADispatcherLinksTheDriverOnlyAtTheRevisionTheySaw()
  {
    await using var f = await ReplyFixture.CreateAsync();
    var (conversation, _) = await f.ConversationAsync();
    var driver = await DriverAsync(f, "Ann Driver");
    Assert.Equal(
      ContextStates.Unmatched,
      (await ContextAsync(f, conversation)).State
    );
    var revision = await f.RevisionAsync(conversation);
    using var signals = f.Events.Subscribe(Company.Amf);

    Assert.Equal(
      409,
      (
        await Link(f).Handle(new(conversation, driver, revision - 1), default)
      ).StatusCode
    );
    Assert.Equal(
      404,
      (
        await Link(f)
          .Handle(new(conversation, Guid.NewGuid(), revision), default)
      ).StatusCode
    );
    var linked = await Link(f)
      .Handle(new(conversation, driver, revision), default);

    Assert.Equal(revision + 1, linked.Response);
    var context = await ContextAsync(f, conversation);
    Assert.Equal(
      ("Ann Driver", ContextStates.NoTruck),
      (context.DriverName, context.State)
    );
    Assert.True(signals.Reader.TryRead(out var signal));
    Assert.Equal(
      (conversation, revision + 1),
      (signal!.ConversationId, signal.Revision)
    );
  }

  [Fact]
  public async Task ACoDriverOnALiveLegIsOnThatTruck()
  {
    await using var f = await ReplyFixture.CreateAsync();
    var (conversation, driver) = await LinkedAsync(f);
    var truck = await TruckAsync(f, "11006");
    var other = await DriverAsync(f, "Bob Driver");
    await LegAsync(f, truck, other, driver, "active");
    await LegAsync(f, await TruckAsync(f, "11007"), driver, null, "completed");

    var context = await ContextAsync(f, conversation);

    Assert.Equal(ContextStates.OneTruck, context.State);
    Assert.Equal(
      new ContextTruck(truck, "11006", "co-driver"),
      Assert.Single(context.Trucks)
    );
  }

  [Fact]
  public async Task TwoTrucksAreShownAndNoLoadIsOffered()
  {
    await using var f = await ReplyFixture.CreateAsync();
    var (conversation, driver) = await LinkedAsync(f);
    await LegAsync(f, await TruckAsync(f, "11006"), driver, null, "active");
    await LegAsync(f, await TruckAsync(f, "11007"), null, driver, "planned");

    var context = await ContextAsync(f, conversation);

    Assert.Equal(ContextStates.SeveralTrucks, context.State);
    Assert.Equal(
      [("11006", "driver"), ("11007", "co-driver")],
      context.Trucks.Select(x => (x.Number, x.Role))
    );
    Assert.Empty(context.Loads);
  }

  // The fleet holds a driver on at most one truck; a live leg elsewhere
  // outranks it.
  [Fact]
  public async Task WithoutALiveLegTheFleetAssignmentDecides()
  {
    await using var f = await ReplyFixture.CreateAsync();
    var (conversation, driver) = await LinkedAsync(f);
    await TruckAsync(f, "11006", driver);

    var assigned = await ContextAsync(f, conversation);
    await LegAsync(f, await TruckAsync(f, "11007"), driver, null, "active");
    var driving = await ContextAsync(f, conversation);

    Assert.Equal(
      (ContextStates.OneTruck, "11006", "assigned"),
      (
        assigned.State,
        Assert.Single(assigned.Trucks).Number,
        assigned.Trucks[0].Role
      )
    );
    Assert.Equal(
      ("11007", "driver"),
      (Assert.Single(driving.Trucks).Number, driving.Trucks[0].Role)
    );
  }

  private static async Task<(Guid Conversation, Guid Driver)> LinkedAsync(
    ReplyFixture f
  )
  {
    var (conversation, _) = await f.ConversationAsync();
    var driver = await DriverAsync(f, "Ann Driver");
    var linked = await Link(f)
      .Handle(
        new(conversation, driver, await f.RevisionAsync(conversation)),
        default
      );
    Assert.True(linked.Success);
    return (conversation, driver);
  }

  private static async Task<Guid> DriverAsync(ReplyFixture f, string name)
  {
    var driver = new Driver
    {
      Id = Guid.NewGuid(),
      ExternalId = name,
      Name = name,
      IsActive = true,
    };
    f.Db.Drivers.Add(driver);
    await f.Db.SaveChangesAsync();
    f.Db.ChangeTracker.Clear();
    return driver.Id;
  }

  private static async Task<Guid> TruckAsync(
    ReplyFixture f,
    string number,
    Guid? driver = null
  )
  {
    var truck = new Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = number,
      UnitNumber = number,
      IsActive = true,
      DriverId = driver,
    };
    f.Db.Trucks.Add(truck);
    await f.Db.SaveChangesAsync();
    f.Db.ChangeTracker.Clear();
    return truck.Id;
  }

  private static async Task LegAsync(
    ReplyFixture f,
    Guid truck,
    Guid? driver,
    Guid? coDriver,
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
    f.Db.ChangeTracker.Clear();
  }

  private static async Task<ConversationContext> ContextAsync(
    ReplyFixture f,
    Guid conversation
  )
  {
    f.Db.ChangeTracker.Clear();
    var result = await new ConversationContextHandler(
      f.Db,
      new ReplyFixture.Caller("me"),
      new FleetNames(f.Db),
      new ActiveTransfers(f.Db),
      f.Clock
    ).Handle(new(conversation), default);
    Assert.True(result.Success, string.Join(";", result.Errors ?? []));
    return result.Response!;
  }

  private static SetConversationDriverHandler Link(ReplyFixture f)
  {
    f.Db.ChangeTracker.Clear();
    return new(
      f.Db,
      new ReplyFixture.Caller("me"),
      new TestCompany(),
      f.Events
    );
  }
}
