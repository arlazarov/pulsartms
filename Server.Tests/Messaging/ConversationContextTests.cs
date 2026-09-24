using Application.Features.Execution.Queries;
using Application.Features.Messaging.Commands;
using Application.Features.Messaging.Queries;
using Application.Interfaces;
using Application.Reference;
using Domain.Entities;
using Domain.Entities.Execution;
using Domain.Entities.Fleet;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Messaging;

// Beside a conversation, as the API composes it: the driver (Messaging),
// linked by a dispatcher when the number matched nobody, and the truck they
// are driving (Execution) - from their planned and active execution legs as
// driver or co-driver, else from the fleet's assignment. More than one
// truck is shown as such, with no loads offered.
[Trait("Category", "Messaging")]
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
      DriverWorkStates.Unmatched,
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
      ("Ann Driver", DriverWorkStates.NoTruck),
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

    Assert.Equal(DriverWorkStates.OneTruck, context.State);
    Assert.Equal(
      new DriverTruck(truck, "11006", "co-driver"),
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

    Assert.Equal(DriverWorkStates.SeveralTrucks, context.State);
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
      (DriverWorkStates.OneTruck, "11006", "assigned"),
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

  // The conversation's context as the API composes it: who the driver is
  // (Messaging), then what they are driving (Execution).
  private static async Task<Context> ContextAsync(
    ReplyFixture f,
    Guid conversation
  )
  {
    f.Db.ChangeTracker.Clear();
    var driver = await new ConversationDriverHandler(
      f.Db,
      new ReplyFixture.Caller("me")
    ).Handle(new(conversation), default);
    Assert.True(driver.Success, string.Join(";", driver.Errors ?? []));
    var work = await new DriverWorkHandler(
      f.Db,
      new ReplyFixture.Caller("me"),
      new DispatchRole(),
      new FleetNames(f.Db),
      new ActiveTransfers(f.Db),
      f.Clock
    ).Handle(new(driver.Response!.DriverId), default);
    Assert.True(work.Success, string.Join(";", work.Errors ?? []));
    return new(
      driver.Response.DriverName,
      work.Response!.State,
      work.Response.Trucks,
      work.Response.Loads
    );
  }

  private sealed record Context(
    string? DriverName,
    string State,
    IReadOnlyList<DriverTruck> Trucks,
    IReadOnlyList<DriverLoad> Loads
  );

  private sealed class DispatchRole : IUserRoleService
  {
    public Task<string?> GetAsync(string id, CancellationToken ct = default) =>
      Task.FromResult<string?>("Dispatch");

    public Task<Dictionary<Guid, string>> GetAsync(
      IReadOnlyCollection<Guid> ids,
      CancellationToken ct = default
    ) => throw new NotSupportedException();

    public Task SetAsync(
      string id,
      string role,
      CancellationToken ct = default
    ) => throw new NotSupportedException();
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
