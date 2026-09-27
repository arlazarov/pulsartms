using Application.Features.Routing.Services.Deadheads;
using Application.Features.Routing.Services.FuelPlanning;
using Domain.Models.Routing;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Server.Tests.Support;

namespace Server.Tests.Fuel;

// Stage 4e of docs/architecture/current-work.md: one planning refresh
// checked the same saved fuel plan against the same saved roads and
// history once per caller. Inside an operation that shares the check
// (FuelSavedInputsValidation.Share), the same saved plan, remaining roads
// and loads are checked once, while no commit this process knows of has
// touched the truck's inputs; outside one, every call checks as before.
[Trait("Category", "Fuel")]
[Trait("Kind", "Integration")]
public sealed class FuelSavedInputsShareTests
{
  // The check alone is 6 statements on this fixture (FuelCallerCostTests).
  private const int Check = 6;

  [Fact]
  public async Task UnchangedInputsAreCheckedOncePerOperation()
  {
    await using var t = await Test.CreateAsync();

    using (t.Services.SavedFuelInputs.Share())
    {
      Assert.Equal(Check, await t.ApplyAsync());
      Assert.Equal(0, await t.ApplyAsync());
      Assert.Equal(1, t.LastRecords);
      Assert.Equal(0, await t.ApplyAsync(supplied: false));
    }
    var outside = await t.ApplyAsync();

    Assert.Equal(Check, outside);
    Assert.False(t.Last.Plan!.FuelPlan!.NeedsRefresh);
  }

  // A base road committed through the publication, and a connection
  // through its own: both drop the shared answer, and the next caller
  // finds the change.
  [Theory]
  [InlineData("road")]
  [InlineData("connection")]
  public async Task AnAnnouncedCommitIsCheckedAgainAndFound(string change)
  {
    await using var t = await Test.CreateAsync();
    using var share = t.Services.SavedFuelInputs.Share();
    Assert.Equal(Check, await t.ApplyAsync());
    Assert.False(t.Last.Plan!.FuelPlan!.NeedsRefresh);

    await using (
      var transaction = await t.Fixture.Db.Database.BeginTransactionAsync()
    )
    {
      if (change == "road")
      {
        await t.Fixture.Db.DispatchBaseRoutes.ExecuteUpdateAsync(s =>
          s.SetProperty(x => x.CalculatedAt, DateTime.UtcNow.AddMinutes(1))
        );
        await t.Services.Publication.CommitAsync(transaction, t.Truck, default);
      }
      else
      {
        await t.Fixture.Db.DispatchDeadheads.ExecuteUpdateAsync(s =>
          s.SetProperty(x => x.InputHash, "corrected road")
        );
        await new DeadheadHistoryPublication(
          t.Services.DeadheadHistory,
          new PlanningPublicationScope(t.Fixture.Db),
          t.Services.Summaries,
          new TestCompany(),
          t.Services.Reads
        ).CommitAsync(transaction, t.Truck, default);
      }
    }
    var after = await t.ApplyAsync();

    // Checked again; a road found changed ends the check early.
    Assert.InRange(after, 1, Check);
    Assert.True(t.Last.Plan!.FuelPlan!.NeedsRefresh);
  }

  // Another process wrote the road and its announcement has not arrived:
  // the operation keeps the answer it has - the window is the operation -
  // and the next operation finds the change.
  [Fact]
  public async Task AnUnannouncedWriteIsFoundByTheNextOperation()
  {
    await using var t = await Test.CreateAsync();
    using (t.Services.SavedFuelInputs.Share())
    {
      await t.ApplyAsync();
      await t.Fixture.Db.DispatchBaseRoutes.ExecuteUpdateAsync(s =>
        s.SetProperty(x => x.CalculatedAt, DateTime.UtcNow.AddMinutes(1))
      );
      Assert.Equal(0, await t.ApplyAsync());
      Assert.False(t.Last.Plan!.FuelPlan!.NeedsRefresh);
    }

    using (t.Services.SavedFuelInputs.Share())
      Assert.InRange(await t.ApplyAsync(), 1, Check);

    Assert.True(t.Last.Plan!.FuelPlan!.NeedsRefresh);
  }

  // A commit announced while the check runs: its answer was read across
  // the commit and is not shared.
  [Fact]
  public async Task AnAnswerReadAcrossACommitIsNotShared()
  {
    await using var t = await Test.CreateAsync();
    await t.ApplyAsync();
    using var share = t.Services.SavedFuelInputs.Share();
    t.Reads.BeforeFirstRead = () =>
      t.Services.Reads.InvalidateItem("planning-inputs", t.Truck);

    // The first statement of a warm call is the check's own.
    var first = await t.ApplyAsync(before: () => t.Reads.Enabled = true);
    t.Reads.Enabled = false;
    var second = await t.ApplyAsync();

    Assert.Equal(Check, first);
    Assert.Equal(Check, second);
    Assert.Equal(0, await t.ApplyAsync());
  }

  // Two operations for the same truck one after the other each check
  // once: an answer is not carried from one to the next.
  [Fact]
  public async Task OperationsOneAfterAnotherEachCheck()
  {
    await using var t = await Test.CreateAsync();
    var validation = t.Services.SavedFuelInputs;

    using (validation.Share())
      Assert.Equal(Check, await t.ApplyAsync());
    using (validation.Share())
      Assert.Equal(Check, await t.ApplyAsync());
  }

  // Two operations open at once, each in its own scope (its own owner
  // instance), their calls interleaved: neither answers from the other's
  // check, so each checks once and then reuses only its own.
  [Fact]
  public async Task OverlappingOperationsDoNotShareAnAnswer()
  {
    await using var t = await Test.CreateAsync();
    var first = t.Services.SavedFuelInputs;
    var second = t.OtherScope();
    var saved = await t.SavedAsync();
    var plan = t.Last.Plan!;

    using var a = first.Share();
    using var b = second.Share();
    var costs = new[]
    {
      await t.CountAsync(() => first.MatchesAsync(saved, plan, default)),
      await t.CountAsync(() => second.MatchesAsync(saved, plan, default)),
      await t.CountAsync(() => first.MatchesAsync(saved, plan, default)),
      await t.CountAsync(() => second.MatchesAsync(saved, plan, default)),
    };

    Assert.Equal([Check, Check, 0, 0], costs);
  }

  // The same saved plan - truck, calculation time and roads - with another
  // history signature is another check: the answer for the first is not
  // the answer for it.
  [Fact]
  public async Task AnotherHistorySignatureIsCheckedAgain()
  {
    await using var t = await Test.CreateAsync();
    var validation = t.Services.SavedFuelInputs;
    var saved = await t.SavedAsync();
    var history = saved.HistoryDependencies!;
    var changed = saved with
    {
      HistoryDependencies = history with
      {
        Batches =
        [
          .. history.Batches.Select(batch =>
            batch with
            {
              Inputs =
              [
                .. batch.Inputs.Select(x =>
                  x with
                  {
                    InputSignature = "another history",
                  }
                ),
              ],
            }
          ),
        ],
      },
    };
    var plan = t.Last.Plan!;
    using var share = validation.Share();

    Assert.True(await validation.MatchesAsync(saved, plan, default));
    var cost = await t.CountAsync(
      async () =>
        Assert.False(await validation.MatchesAsync(changed, plan, default))
    );

    Assert.Equal(changed.CalculatedAt, saved.CalculatedAt);
    Assert.InRange(cost, 1, Check);
  }

  // One share per scope: a second is refused and leaves the first as it
  // was; ending the first again - even after a new one began - is
  // harmless.
  [Fact]
  public async Task ANestedShareIsRefusedAndTheOuterOneKept()
  {
    await using var t = await Test.CreateAsync();
    var validation = t.Services.SavedFuelInputs;

    var outer = validation.Share();
    await t.ApplyAsync();
    Assert.Throws<InvalidOperationException>(() => validation.Share());
    var kept = await t.ApplyAsync();
    outer.Dispose();
    outer.Dispose();
    var ended = await t.ApplyAsync();
    using (validation.Share())
    {
      // A late end of the first share does not end this one.
      outer.Dispose();
      Assert.Equal(Check, await t.ApplyAsync());
      Assert.Equal(0, await t.ApplyAsync());
    }

    Assert.Equal(0, kept);
    Assert.Equal(Check, ended);
  }

  private sealed class Test : IAsyncDisposable
  {
    private readonly QueryColumnProbe probe;

    private Test(
      SavedFuelHorizonFixture fixture,
      QueryColumnProbe probe,
      HistoricalReadProbe reads,
      Domain.Models.Execution.TruckItinerarySnapshot itinerary
    )
    {
      Fixture = fixture;
      this.probe = probe;
      Reads = reads;
      Itinerary = itinerary;
    }

    public SavedFuelHorizonFixture Fixture { get; }
    public PlanningTestServices Services => Fixture.Services;
    public HistoricalReadProbe Reads { get; }
    public Domain.Models.Execution.TruckItinerarySnapshot Itinerary { get; }
    public Guid Truck => Fixture.State.Plan!.TruckId;
    public RoutePlanningState Last { get; private set; } = null!;

    // The hand-over records read of the last projection. It is fresh on
    // every projection, shared or not (audit D6), and not part of the
    // check these tests count.
    public int LastRecords { get; private set; }

    public static async Task<Test> CreateAsync()
    {
      var probe = new QueryColumnProbe();
      var reads = new HistoricalReadProbe();
      var f = await SavedFuelHorizonFixture.CreateAsync(
        historyReads: reads,
        queryColumns: probe
      );
      var profile = await f.PrepareCalculationAsync();
      var root = f.State.Plan!;
      await f.Services.Fuel.BuildAsync(
        f.Current.Id,
        new(profile)
        {
          ExecutionLegId = root.ExecutionLegId,
          AssignmentRevision = root.AssignmentRevision,
        },
        default
      );
      var work = await f.Services.PlanningInputs.ReadFreshAsync(
        root.TruckId,
        default
      );
      var test = new Test(f, probe, reads, work!.Itinerary);
      // Warm: the saved plan, its geometry and the itinerary are cached,
      // so what a call costs after this is the check.
      await test.ApplyAsync();
      await test.ApplyAsync(supplied: false);
      return test;
    }

    // One caller's projection on a fresh state; the statements it ran.
    public async Task<int> ApplyAsync(
      bool supplied = true,
      Action? before = null
    )
    {
      var plan = Fixture.State.Plan!;
      var load = await Services.Routes.LoadAsync(
        Fixture.Current.Id,
        default,
        plan.ExecutionLegId
      );
      var state = await Services.Routes.GetAsync(load, default);
      probe.Clear();
      before?.Invoke();
      await Services.FuelPlans.ApplyAsync(
        state,
        default,
        supplied ? Itinerary : null
      );
      Last = state;
      LastRecords = probe.Statements.Count(Records);
      return probe.Statements.Count(sql => !Records(sql));
    }

    private static bool Records(string sql) =>
      sql.Contains("\"FuelVisitSends\"");

    public async Task<int> CountAsync(Func<Task> call)
    {
      probe.Clear();
      await call();
      return probe.Statements.Count;
    }

    public async Task<TruckFuelPlanSnapshot> SavedAsync() =>
      (await Services.FuelPlans.ReadAsync(Truck, default))!;

    // The owner as another scope has it, over the same database.
    public FuelSavedInputsValidation OtherScope() =>
      new(
        Services.Roads,
        Services.DeadheadHistory,
        new ExecutionReadScope(Fixture.Db),
        Services.Reads
      );

    public ValueTask DisposeAsync() => Fixture.DisposeAsync();
  }
}
