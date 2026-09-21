using Application.Features.Dispatch.Commands;
using Application.Features.Dispatch.Models;
using Application.Features.Routing.Services.Routes;
using Application.Interfaces;
using Domain.Entities;
using Domain.Rules;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Routing;

public partial class AutomaticPlanningTests
{
  [Theory]
  [InlineData(false, "in_transit")]
  [InlineData(true, "in_transit")]
  [InlineData(false, "assigned")]
  [InlineData(true, "assigned")]
  public async Task ConfirmingPickupKeepsExistingRoadThroughReadsAndAutomaticPolling(
    bool fromCurrent,
    string status
  )
  {
    await using var f = await Fixture.CreateAsync();
    f.Load.Status = status;
    await f.Db.SaveChangesAsync();
    var profile = await f.Plans.ProfileAsync(f.Truck.Id, default);
    var before = await f.Plans.BuildAsync(
      f.Load.Id,
      new(profile, fromCurrent, fromCurrent ? 1 : null),
      default
    );
    var actor = new User
    {
      Id = Guid.NewGuid(),
      IdentityUserId = "completion-operator",
      Name = "Fixture",
      Email = "fixture@example.invalid",
    };
    f.Db.Users.Add(actor);
    await f.Db.SaveChangesAsync();
    var stop = f.Load.Stops[0];
    var handler = new SetStopCompletionHandler(
      f.Db,
      new CompletionCaller(),
      new CompletionRoles(),
      TimeProvider.System,
      f.Services.Reads,
      TestCache.Preparation(),
      f.Plans
    );
    var identity = StopCompletionIdentity.Create(
      stop.Id,
      stop.Sequence,
      stop.Job,
      stop.Address,
      stop.City,
      stop.Province,
      stop.Country,
      stop.Name,
      stop.TruckId,
      stop.ScheduledDate,
      stop.ScheduledTime
    );
    var result = await handler.Handle(
      new(
        f.Load.Id,
        stop.Id,
        new(DateTimeOffset.UtcNow.AddMinutes(-1), 0, identity)
      ),
      default
    );
    Assert.True(result.Success);
    var calls = f.Router.Calls;

    var read = await f.Plans.GetAsync(f.Load.Id, default);
    Assert.False(read.Plan!.InputsChanged);
    Assert.Equal(f.Load.Stops[1].Id, read.Plan.Tracking.NextStopId);
    for (var i = 0; i < 3; i++)
    {
      var polled = await f.Service.ForTruckAsync(f.Truck.Id, default);
      Assert.Null(polled.Message);
      Assert.False(polled.State!.Plan!.InputsChanged);
      Assert.Equal(before.Version, polled.State.Plan.Version);
      Assert.Equal(before.CalculatedAt, polled.State.Plan.CalculatedAt);
      Assert.Equal(
        RoutePlanStorage.Serialize(before.Route),
        RoutePlanStorage.Serialize(polled.State.Plan.Route)
      );
      Assert.Equal(calls, f.Router.Calls);
    }
    var undo = await handler.Handle(
      new(f.Load.Id, stop.Id, new(null, 1, identity)),
      default
    );
    Assert.True(undo.Success);
    var restored = await f.Service.ForDispatchAsync(f.Load.Id, default);
    Assert.Null(restored.Message);
    Assert.False(restored.State!.Plan!.InputsChanged);
    Assert.Equal(before.Version, restored.State.Plan.Version);
    Assert.Equal(calls, f.Router.Calls);
    Assert.Equal(stop.Id, restored.State.Plan.Tracking.NextStopId);
  }

  private sealed class CompletionCaller : ICurrentUser
  {
    public bool IsAuthenticated => true;
    public string IdentityUserId => "completion-operator";
  }

  private sealed class CompletionRoles : IUserRoleService
  {
    public Task<string?> GetAsync(
      string identityUserId,
      CancellationToken ct = default
    ) => Task.FromResult<string?>("Dispatch");

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
