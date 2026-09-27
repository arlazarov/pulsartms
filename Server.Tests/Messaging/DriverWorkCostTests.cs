using Application.Features.Execution.Queries;
using Application.Interfaces;
using Application.Reference;
using Domain.Entities.Fleet;
using Server.Tests.Support;
using Xunit.Abstractions;

namespace Server.Tests.Messaging;

// What Messenger's driver work costs in round trips once it asks planning
// for the current load. Counted, not timed. Warm, planning answers from the
// shared "planning-inputs" read the planning summary also uses, and the
// handler sends what it sent before (5). Cold, planning captures the truck's
// itinerary, saved plans, profiles and driver itself (12 more); three of
// them (trucks, native work, loads) repeat the board read, asked with other
// filters inside planning's own snapshot. Raising either needs a reason.
[Trait("Category", "Messaging")]
[Trait("Kind", "Integration")]
public sealed class DriverWorkCostTests(ITestOutputHelper output)
{
  [Fact]
  public async Task PlanningCostsOnlyWhenItsSharedReadIsCold()
  {
    var probe = new QueryColumnProbe();
    await using var f = await SavedFuelHorizonFixture.CreateAsync(
      queryColumns: probe
    );
    var current = await f.ReceiveCurrentAsync();
    var driver = new Driver
    {
      Id = Guid.NewGuid(),
      ExternalId = "cost-driver",
      Name = "Cost Driver",
      IsActive = true,
    };
    f.Db.Drivers.Add(driver);
    current.DriverId = driver.Id;
    f.Db.Users.Add(
      new()
      {
        Id = Guid.NewGuid(),
        IdentityUserId = "dispatcher",
        IsActive = true,
      }
    );
    await f.Db.SaveChangesAsync();
    var handler = new DriverWorkHandler(
      f.Db,
      new ReplyFixture.Caller("dispatcher"),
      new DispatchRole(),
      // Scoped in the application, so shared with planning as here.
      f.Services.Names,
      f.Services.Transfers,
      TimeProvider.System,
      f.Services.PlanningInputs
    );

    probe.Clear();
    Assert.True((await handler.Handle(new(driver.Id), default)).Success);
    var cold = probe.Statements.ToArray();
    probe.Clear();
    Assert.True((await handler.Handle(new(driver.Id), default)).Success);
    var warm = probe.Statements.ToArray();
    probe.Clear();
    await f.Services.PlanningInputs.ReadFreshAsync(current.TruckId, default);
    var planningAlone = probe.Statements.ToArray();

    output.WriteLine(
      $"cold {cold.Length}, warm {warm.Length}, planning alone "
        + $"{planningAlone.Length}"
    );
    foreach (var statement in cold)
      output.WriteLine("COLD " + First(statement));
    Assert.Equal(StatementsWarm, warm.Length);
    Assert.Equal(StatementsWarm + PlanningCaptureCold, cold.Length);
    Assert.DoesNotContain(
      warm,
      x => x.Contains("DispatchRoutePlans") || x.Contains("PlanningProfiles")
    );
  }

  private const int StatementsWarm = 5;
  private const int PlanningCaptureCold = 12;

  private static string First(string statement)
  {
    var line = statement.Split('\n')[0];
    return line[..Math.Min(90, line.Length)];
  }

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
}
