using Application.Features.Dispatch.Queries;
using Application.Features.Execution.Models;
using Application.Features.Execution.Services;
using Application.Reference;
using Domain.Entities.Execution;
using Domain.Entities.Fleet;
using Microsoft.EntityFrameworkCore;
using Server.Tests.Support;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Integration")]
public sealed class ExecutionWorkReaderTests
{
  private static readonly DateOnly Today = new(2026, 9, 16);

  [Fact]
  public async Task StartedWorkPrecedesAnEarlierUnstartedAppointment()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var truck = await AssignAsync(f);
    f.Load.Status = "in_transit";
    f.Load.ShipDate = Today.AddDays(2);
    var earlier = ScheduledLoad(truck, 1, Today.AddDays(-2));
    f.Db.Dispatches.Add(earlier);
    await f.Db.SaveChangesAsync();

    var work = await ReadAsync(f, truck);

    Assert.Equal(new[] { f.Load.Id, earlier.Id }, work.Loads.Select(x => x.Id));
    Assert.Equal(WorkActivity.Started, work.Loads[0].Order.Activity);
    Assert.Equal(WorkActivity.Upcoming, work.Loads[1].Order.Activity);
  }

  [Fact]
  public async Task SchedulePrecedesLoadNumberAndUnknownDatesFollowKnownDates()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var truck = await AssignAsync(f);
    f.Load.LoadNumber = 1;
    var earlier = ScheduledLoad(truck, 900, Today);
    var later = ScheduledLoad(truck, 2, Today.AddDays(1));
    f.Db.Dispatches.AddRange(later, earlier);
    await f.Db.SaveChangesAsync();

    var work = await ReadAsync(f, truck);

    Assert.Equal(
      new[] { earlier.Id, later.Id, f.Load.Id },
      work.Loads.Select(x => x.Id)
    );
    Assert.Equal(
      Today.ToDateTime(TimeOnly.MinValue),
      work.Loads[0].Order.ScheduledLocalStart
    );
  }

  [Fact]
  public async Task EqualSchedulesRetainTheLoadNumberTieBreak()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var truck = await AssignAsync(f);
    f.Load.ShipDate = Today;
    var first = ScheduledLoad(truck, 10, Today);
    var second = ScheduledLoad(truck, 20, Today);
    f.Db.Dispatches.AddRange(second, first);
    await f.Db.SaveChangesAsync();

    var work = await ReadAsync(f, truck);

    Assert.Equal(
      new[] { first.Id, second.Id, f.Load.Id },
      work.Loads.Select(x => x.Id)
    );
    Assert.Equal(work.Loads[0].Order.Activity, work.Loads[1].Order.Activity);
    Assert.Equal(
      work.Loads[0].Order.ScheduledLocalStart,
      work.Loads[1].Order.ScheduledLocalStart
    );
  }

  [Fact]
  public async Task RepeatedVisitsAndBoardFilteringDoNotChangeWorkIdentity()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var truck = await AssignAsync(f);
    var before = await ReadAsync(f, truck);
    var load = Assert.Single(before.Loads);
    Assert.Equal(f.Load.Stops.Select(x => x.Id), load.Visits.Select(x => x.Id));
    Assert.Equal(5, load.Visits.Select(x => x.Id).Distinct().Count());
    var board = await f.Planning.Board.Handle(
      new(
        TruckId: truck.Id,
        Date: Today,
        IncludeHos: false,
        IncludeFinancials: false,
        IncludeEta: false
      ),
      default
    );
    Assert.Equal(
      load.Id,
      Assert.Single(Assert.Single(board.Response!.Items).Dispatches).Id
    );
    var hidden = await f.Planning.Board.Handle(
      new(
        Search: "does-not-match",
        TruckId: truck.Id,
        Date: Today,
        IncludeHos: false,
        IncludeFinancials: false,
        IncludeEta: false
      ),
      default
    );
    Assert.Empty(hidden.Response!.Items);
    var after = await ReadAsync(f, truck);
    Assert.Equal(load.Id, Assert.Single(after.Loads).Id);
    Assert.Equal(
      load.Visits.ToArray(),
      Assert.Single(after.Loads).Visits.ToArray()
    );
  }

  [Fact]
  public async Task UnstartedOverdueWorkIsNotLostToBoardDateFiltering()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var truck = await AssignAsync(f);
    f.Load.ShipDate = f.Load.DeliveryDate = Today.AddDays(-3);
    await f.Db.SaveChangesAsync();
    var work = await ReadAsync(f, truck);
    Assert.Equal(f.Load.Id, Assert.Single(work.Loads).Id);
    var board = await f.Planning.Board.Handle(
      new(
        TruckId: truck.Id,
        Date: Today,
        IncludeHos: false,
        IncludeFinancials: false,
        IncludeEta: false
      ),
      default
    );
    Assert.Empty(Assert.Single(board.Response!.Items).Dispatches);
    f.Load.Stops[^1].DeliveredAt = Today.ToDateTime(TimeOnly.MinValue);
    await f.Db.SaveChangesAsync();
    Assert.Empty((await ReadAsync(f, truck)).Loads);
  }

  [Fact]
  public async Task NativeLegsKeepSeparateIdentitiesAndRevisionSnapshots()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var truck = await AssignAsync(f);
    var trip = new Trip { Id = Guid.NewGuid() };
    var first = new ExecutionLeg
    {
      Id = Guid.NewGuid(),
      Trip = trip,
      TruckId = truck.Id,
      Status = "active",
      Revision = 7,
      Stops = ExecutionStopRows.Capture(f.Load.Stops.Take(2)),
    };
    var next = new ExecutionLeg
    {
      Id = Guid.NewGuid(),
      Trip = trip,
      TruckId = truck.Id,
      Status = "planned",
      Revision = 2,
      Stops = ExecutionStopRows.Capture(f.Load.Stops.Skip(2)),
    };
    f.Db.LoadExecutionLegs.AddRange(
      new LoadExecutionLeg
      {
        Id = Guid.NewGuid(),
        DispatchId = f.Load.Id,
        ExecutionLeg = first,
        Sequence = 1,
      },
      new LoadExecutionLeg
      {
        Id = Guid.NewGuid(),
        DispatchId = f.Load.Id,
        ExecutionLeg = next,
        Sequence = 2,
      }
    );
    await f.Db.SaveChangesAsync();
    var before = await ReadAsync(f, truck);
    Assert.Equal(
      new[] { first.Id, next.Id },
      before.Loads.Select(x => x.ExecutionLegId!.Value)
    );
    Assert.Equal(
      new long[] { 7, 2 },
      before.Loads.Select(x => x.AssignmentRevision)
    );
    Assert.Equal(WorkActivity.ActiveExecution, before.Loads[0].Order.Activity);
    Assert.Equal(WorkActivity.Upcoming, before.Loads[1].Order.Activity);
    first.Revision = 8;
    await f.Db.SaveChangesAsync();
    var after = await ReadAsync(f, truck);
    Assert.Equal(8, after.Loads[0].AssignmentRevision);
    Assert.Equal(7, before.Loads[0].AssignmentRevision);
    Assert.Equal(5, before.Loads.Sum(x => x.Visits.Length));
  }

  [Fact]
  public async Task NumberOnlyAssignmentsRemainReadableWithoutTrackedWrites()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var truck = await AssignAsync(f);
    f.Load.TruckId = null;
    f.Load.TruckNumber = "  reader  ";
    await f.Db.SaveChangesAsync();
    f.Db.ChangeTracker.Clear();
    Assert.Equal(
      f.Load.Id,
      Assert.Single((await ReadAsync(f, truck)).Loads).Id
    );
    Assert.Empty(f.Db.ChangeTracker.Entries());
  }

  private static async Task<Truck> AssignAsync(StopCompletionFixture f)
  {
    var truck = new Truck
    {
      Id = Guid.NewGuid(),
      UnitNumber = "Reader",
      IsActive = true,
    };
    f.Db.Trucks.Add(truck);
    f.Load.TruckId = truck.Id;
    await f.Db.SaveChangesAsync();
    return truck;
  }

  // The board's current-or-upcoming rule decides which loads a dispatcher
  // sees. These pin each branch, because the selection is applied in memory
  // over loaded rows and any attempt to push it into SQL must keep every
  // answer identical.
  [Theory]
  [InlineData(-3, false, false)]
  [InlineData(-3, true, true)]
  [InlineData(0, false, true)]
  [InlineData(3, false, true)]
  public async Task AnUnstartedLoadIsSelectedByItsDeliveryDate(
    int days,
    bool started,
    bool selected
  )
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var truck = await AssignAsync(f);
    // A leg-based load ignores dates, so the date branch needs a source load.
    var load = ScheduledLoad(truck, 7, Today.AddDays(days));
    if (started)
      load.Status = "in_transit";
    f.Db.Dispatches.Add(load);
    await f.Db.SaveChangesAsync();

    var work = await ReadCurrentAsync(f, truck);

    Assert.Equal(
      selected,
      work.SelectMany(x => x.Loads).Any(x => x.Id == load.Id)
    );
  }

  [Fact]
  public async Task AStopActualStartsAnOverdueLoadAndKeepsItSelected()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var truck = await AssignAsync(f);
    var load = ScheduledLoad(truck, 8, Today.AddDays(-3));
    f.Db.Dispatches.Add(load);
    await f.Db.SaveChangesAsync();
    Assert.DoesNotContain(
      (await ReadCurrentAsync(f, truck)).SelectMany(x => x.Loads),
      x => x.Id == load.Id
    );

    load.Stops[0].PickedUpAt = Today.ToDateTime(TimeOnly.MinValue);
    await f.Db.SaveChangesAsync();

    Assert.Contains(
      (await ReadCurrentAsync(f, truck)).SelectMany(x => x.Loads),
      x => x.Id == load.Id
    );
  }

  [Fact]
  public async Task AnExplicitlyIncompleteDeliveryKeepsAStartedLoadSelected()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var truck = await AssignAsync(f);
    f.Load.Status = "in_transit";
    f.Load.Stops[^1].DeliveredAt = Today.ToDateTime(TimeOnly.MinValue);
    f.Load.Stops[^1].CompletionOverride = false;
    await f.Db.SaveChangesAsync();

    var work = await ReadAsync(f, truck);

    Assert.Equal(f.Load.Id, Assert.Single(work.Loads).Id);
  }

  [Fact]
  public async Task AnOverriddenDeliveryCompletesALoadWithoutAnyActual()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var truck = await AssignAsync(f);
    f.Load.Status = "in_transit";
    f.Load.Stops[^1].CompletionOverride = true;
    await f.Db.SaveChangesAsync();

    Assert.Empty((await ReadAsync(f, truck)).Loads);
  }

  private static Load ScheduledLoad(Truck truck, int number, DateOnly day) =>
    new()
    {
      Id = Guid.NewGuid(),
      TruckId = truck.Id,
      LoadNumber = number,
      Status = "assigned",
      ShipDate = day,
      DeliveryDate = day,
      Stops =
      [
        new()
        {
          Id = Guid.NewGuid(),
          Sequence = 1,
          Job = "Pick Up",
          ScheduledDate = day,
        },
        new()
        {
          Id = Guid.NewGuid(),
          Sequence = 2,
          Job = "Drop Off",
          ScheduledDate = day,
        },
      ],
    };

  // The board asks without overdue work; ReadAsync keeps it.
  private static async Task<IReadOnlyList<TruckWorkSelection>> ReadCurrentAsync(
    StopCompletionFixture f,
    Truck truck
  ) =>
    await ExecutionWorkReader.ReadAsync(
      f.Db,
      Today,
      new FleetNames(f.Db),
      new ActiveTransfers(f.Db),
      truck.Id,
      false,
      false,
      default
    );

  private static async Task<TruckWorkSelection> ReadAsync(
    StopCompletionFixture f,
    Truck truck
  ) =>
    Assert.Single(
      await ExecutionWorkReader.ReadAsync(
        f.Db,
        Today,
        new FleetNames(f.Db),
        new ActiveTransfers(f.Db),
        truck.Id,
        false,
        true,
        default
      )
    );
}
