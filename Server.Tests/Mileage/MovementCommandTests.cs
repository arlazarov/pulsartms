using Application.Features.Mileage.Commands;
using Application.Features.Mileage.Models;
using Application.Interfaces;
using Domain.Entities.Fleet;
using Domain.Entities.Mileage;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Mileage;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Integration")]
public sealed class MovementCommandTests
{
  [Fact]
  public async Task SameRetryKeyReturnsOneMovementAndOneAllocation()
  {
    await using var fixture = await StopCompletionFixture.CreateAsync();
    var request = await RequestAsync(fixture);
    var handler = Recorder(fixture);
    var first = await handler.Handle(new(request), default);
    var retry = await handler.Handle(new(request), default);
    Assert.True(first.Success);
    Assert.True(retry.Success);
    Assert.Equal(first.Response!.MovementId, retry.Response!.MovementId);
    Assert.Equal("11005", retry.Response.TruckNumber);
    Assert.Equal(fixture.Load.LoadNumber, retry.Response.PreviousLoadNumber);
    Assert.Equal(1, await fixture.Db.Movements.CountAsync());
    Assert.Equal(1, await fixture.Db.MovementAllocationEvents.CountAsync());
    Assert.Equal(
      409,
      (
        await handler.Handle(
          new(request with { Purpose = "maintenance" }),
          default
        )
      ).StatusCode
    );
    Assert.Equal(1, await fixture.Db.Movements.CountAsync());
  }

  [Fact]
  public async Task PlannedAndManualActualKeepIndependentVersionedEvidence()
  {
    await using var fixture = await StopCompletionFixture.CreateAsync();
    var request = await RequestAsync(fixture);
    var recorded = await Recorder(fixture).Handle(new(request), default);
    var id = recorded.Response!.MovementId!.Value;
    var handler = Distances(fixture);
    var planned = await handler.Handle(
      new(id, Estimate(fixture, 1, 45)),
      default
    );
    Assert.True(planned.Success);
    Assert.Null(planned.Response!.ActualMiles);
    Assert.Null(planned.Response.StartedAt);

    var actual = await handler.Handle(
      new(id, Actual(fixture, 2, 42, -4, -2)),
      default
    );
    Assert.True(actual.Success);
    Assert.Equal(45m, actual.Response!.PlannedMiles);
    Assert.Equal(42m, actual.Response.ActualMiles);
    Assert.Equal("manual-distance-record", actual.Response.ActualSource);
    Assert.Equal(
      fixture.Clock.GetUtcNow().AddHours(-4).UtcDateTime,
      actual.Response.StartedAt
    );
    var nextEstimate = await handler.Handle(
      new(id, Estimate(fixture, 3, 44)),
      default
    );
    Assert.True(nextEstimate.Success);
    Assert.Equal(44m, nextEstimate.Response!.PlannedMiles);
    Assert.Equal(42m, nextEstimate.Response.ActualMiles);
    var evidence = await fixture
      .Db.MovementDistanceEvidence.OrderBy(x => x.Revision)
      .ToListAsync();
    Assert.Equal(new decimal[] { 45, 42, 44 }, evidence.Select(x => x.Miles));
    Assert.Equal(new long[] { 2, 3, 4 }, evidence.Select(x => x.Revision));
    Assert.Null(evidence[0].StartedAt);
    Assert.Equal(actual.Response.StartedAt, evidence[1].StartedAt);
    Assert.Equal(actual.Response.EndedAt, evidence[1].EndedAt);
    Assert.Null(evidence[2].StartedAt);
  }

  [Fact]
  public async Task RecordedIntervalsRejectOverlapAndAllowAdjacency()
  {
    await using var fixture = await StopCompletionFixture.CreateAsync();
    var request = await RequestAsync(fixture);
    var handler = Recorder(fixture);
    var now = fixture.Clock.GetUtcNow();
    Assert.True(
      (
        await handler.Handle(
          new(
            request with
            {
              StartedAt = now.AddHours(-4),
              EndedAt = now.AddHours(-2),
            }
          ),
          default
        )
      ).Success
    );
    Assert.Equal(
      409,
      (
        await handler.Handle(
          new(
            request with
            {
              IdempotencyKey = Guid.NewGuid(),
              StartedAt = now.AddHours(-3),
              EndedAt = now.AddHours(-1),
            }
          ),
          default
        )
      ).StatusCode
    );
    Assert.True(
      (
        await handler.Handle(
          new(
            request with
            {
              IdempotencyKey = Guid.NewGuid(),
              StartedAt = now.AddHours(-2),
              EndedAt = now.AddHours(-1),
            }
          ),
          default
        )
      ).Success
    );
    var unscheduled = await handler.Handle(
      new(request with { IdempotencyKey = Guid.NewGuid() }),
      default
    );
    Assert.True(unscheduled.Success);
    var id = unscheduled.Response!.MovementId!.Value;
    var conflictingActual = await Distances(fixture)
      .Handle(new(id, Actual(fixture, 1, 5, -3, -1)), default);
    Assert.Equal(409, conflictingActual.StatusCode);
    Assert.Equal(3, await fixture.Db.Movements.CountAsync());
    Assert.Empty(await fixture.Db.MovementDistanceEvidence.ToListAsync());
    Assert.Null(
      (await fixture.Db.Movements.SingleAsync(x => x.Id == id)).StartedAt
    );
  }

  [Theory]
  [InlineData("Samsara")]
  [InlineData("telemetry")]
  [InlineData("manual-estimate")]
  public async Task ManualActualCannotImpersonateProviderEvidence(string source)
  {
    await using var fixture = await StopCompletionFixture.CreateAsync();
    var request = await RequestAsync(fixture);
    var row = await Recorder(fixture).Handle(new(request), default);
    var update = Actual(fixture, 1, 8, -3, -2) with { Source = source };
    var response = await Distances(fixture)
      .Handle(new(row.Response!.MovementId!.Value, update), default);
    Assert.Equal(400, response.StatusCode);
    Assert.Empty(await fixture.Db.MovementDistanceEvidence.ToListAsync());
  }

  [Fact]
  public async Task AllocationOverridesAreAuditedAndDoNotChangeDistance()
  {
    await using var fixture = await StopCompletionFixture.CreateAsync();
    var request = await RequestAsync(fixture);
    var row = await Recorder(fixture).Handle(new(request), default);
    var id = row.Response!.MovementId!.Value;
    var handler = new UpdateMovementAllocationHandler(
      fixture.Db,
      new Caller(),
      new Roles(),
      fixture.Clock
    );
    var allocated = await handler.Handle(
      new(id, new(1, "previous", "Agreed return-to-yard exception")),
      default
    );
    Assert.True(allocated.Success);
    Assert.Equal(fixture.Load.Id, allocated.Response!.AllocatedDispatchId);
    Assert.True(allocated.Response.ManualOverride);
    Assert.Null(allocated.Response.PlannedMiles);
    Assert.Null(allocated.Response.ActualMiles);
    Assert.Equal(
      409,
      (
        await handler.Handle(
          new(id, new(1, "unallocated", "Stale editor")),
          default
        )
      ).StatusCode
    );
    var reset = await handler.Handle(
      new(id, new(2, "automatic", "Use current company policy")),
      default
    );
    Assert.True(reset.Success);
    Assert.Null(reset.Response!.AllocatedDispatchId);
    Assert.False(reset.Response.ManualOverride);
    var history = await fixture
      .Db.MovementAllocationEvents.OrderBy(x => x.Revision)
      .ToListAsync();
    Assert.Equal(3, history.Count);
    Assert.Equal(fixture.Actor.Id, history[1].RecordedBy);
    Assert.Null(history[1].PreviousAllocationDispatchId);
    Assert.Equal(fixture.Load.Id, history[2].PreviousAllocationDispatchId);
    Assert.Empty(await fixture.Db.MovementDistanceEvidence.ToListAsync());
  }

  private static async Task<RecordMovementRequest> RequestAsync(
    StopCompletionFixture fixture
  )
  {
    var truck = new Truck { Id = Guid.NewGuid(), UnitNumber = "11005" };
    fixture.Db.Trucks.Add(truck);
    fixture.Actor.IsActive = true;
    await fixture.Db.SaveChangesAsync();
    return new(
      Guid.NewGuid(),
      truck.Id,
      null,
      null,
      null,
      null,
      "home",
      "bobtail",
      fixture.Load.Id,
      null,
      null,
      "Delivery gate",
      "Home yard",
      null,
      null
    );
  }

  private static RecordMovementHandler Recorder(StopCompletionFixture f) =>
    new(f.Db, new Caller(), new Roles(), f.Clock);

  private static UpdateMovementDistanceHandler Distances(
    StopCompletionFixture f
  ) => new(f.Db, new Caller(), new Roles(), f.Clock);

  private static MovementDistanceUpdate Estimate(
    StopCompletionFixture f,
    long revision,
    decimal miles
  ) =>
    new(
      revision,
      "planned",
      miles,
      "manual-estimate",
      "Dispatcher route estimate",
      f.Clock.GetUtcNow(),
      "Recorded planned distance"
    );

  private static MovementDistanceUpdate Actual(
    StopCompletionFixture f,
    long revision,
    decimal miles,
    int startHours,
    int endHours
  ) =>
    new(
      revision,
      "actual",
      miles,
      "manual-distance-record",
      "Driver distance report",
      f.Clock.GetUtcNow(),
      "Dispatcher confirmed report",
      f.Clock.GetUtcNow().AddHours(startHours),
      f.Clock.GetUtcNow().AddHours(endHours)
    );

  private sealed class Caller : ICurrentUser
  {
    public bool IsAuthenticated => true;
    public string IdentityUserId => "operator";
  }

  private sealed class Roles : IUserRoleService
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
