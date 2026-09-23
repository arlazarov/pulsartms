using Application.Features.Routing.Services.FuelPlanning;
using Application.Features.Routing.Services.Routes;
using Application.Interfaces;
using Domain.Models.Routing;
using Domain.Policies;
using Domain.Rules.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Fuel;

// What a driver has been given survives every recalculation, is recorded
// once however often it is confirmed, is never inherited by other work, and
// is seen only by its own carrier.
[Trait("Category", "Fuel")]
[Trait("Kind", "Integration")]
public sealed class FuelIssueRecordsTests
{
  [Fact]
  public async Task AConfirmationIsRecordedOnceAndSurvivesAChangedPlan()
  {
    await using var f = await PlanningRefreshFixture.CreateAsync();
    var (truck, dispatch) = await SeedAsync(f);
    var before = Guid.NewGuid();
    var saved = Snapshot(truck, dispatch, before, revision: 3);
    var visit = Visit(dispatch, before, fill: true);
    var records = Records(f);

    Assert.Equal(1, await records.RecordAsync(saved, [(visit, "text")], "manual", "u1", default));
    Assert.Equal(0, await records.RecordAsync(saved, [(visit, "text")], "manual", "u1", default));
    Assert.Equal(1, await f.Db.FuelVisitSends.CountAsync());

    var shown = Plan(Visit(dispatch, before, fill: true));
    await records.ApplyAsync(saved, shown, null, default);
    Assert.Equal(new FuelSendStatus(f.Now, "u1", "manual", false), shown.Stops[0].Sent);

    // The plan now says forty gallons: the driver was told to fill.
    var changed = Plan(Visit(dispatch, before, gallons: 40));
    await records.ApplyAsync(saved, changed, null, default);
    Assert.True(changed.Stops[0].Sent!.Changed);

    // Sent again as it stands; the first hand-over is kept.
    f.Time.Advance(TimeSpan.FromMinutes(5));
    Assert.Equal(1, await records.RecordAsync(saved, [(changed.Stops[0], "new")], "manual", "u2", default));
    Assert.Equal(2, await f.Db.FuelVisitSends.CountAsync());
    var again = Plan(Visit(dispatch, before, gallons: 40));
    await records.ApplyAsync(saved, again, null, default);
    Assert.Equal(new FuelSendStatus(f.Now, "u2", "manual", false), again.Stops[0].Sent);
  }

  [Fact]
  public async Task OtherWorkNeverInheritsASend()
  {
    await using var f = await PlanningRefreshFixture.CreateAsync();
    var (truck, dispatch) = await SeedAsync(f);
    var before = Guid.NewGuid();
    var records = Records(f);
    await records.RecordAsync(
      Snapshot(truck, dispatch, before, revision: 3),
      [(Visit(dispatch, before, fill: true), "text")],
      "manual",
      "u1",
      default
    );

    // A new assignment of the same load: the same station before the same
    // stop is new work, and was never sent.
    var reassigned = Plan(Visit(dispatch, before, fill: true));
    await records.ApplyAsync(Snapshot(truck, dispatch, before, revision: 4), reassigned, null, default);
    Assert.Null(reassigned.Stops[0].Sent);

    // The same station before another stop is another visit.
    var other = Guid.NewGuid();
    var elsewhere = Plan(Visit(dispatch, other, fill: true));
    await records.ApplyAsync(Snapshot(truck, dispatch, other, revision: 3), elsewhere, null, default);
    Assert.Null(elsewhere.Stops[0].Sent);
  }

  [Fact]
  public async Task AnotherCarrierSeesNothing()
  {
    await using var f = await PlanningRefreshFixture.CreateAsync();
    var (truck, dispatch) = await SeedAsync(f);
    var before = Guid.NewGuid();
    var saved = Snapshot(truck, dispatch, before, revision: 3);
    await Records(f).RecordAsync(saved, [(Visit(dispatch, before, fill: true), "t")], "manual", "u1", default);

    await using var scope = f.NewScope();
    using var serving = scope
      .ServiceProvider.GetRequiredService<ICurrentCompany>()
      .As(Guid.NewGuid());
    var db = scope.ServiceProvider.GetRequiredService<Infrastructure.Persistence.AppDbContext>();
    var foreign = new FuelIssueRecords(
      db,
      new PlanningSummaryCache(f.Time),
      scope.ServiceProvider.GetRequiredService<ICurrentCompany>(),
      Options.Create(new FuelIssueOptions()),
      f.Time
    );
    var shown = Plan(Visit(dispatch, before, fill: true));
    await foreign.ApplyAsync(saved, shown, null, default);
    Assert.Null(shown.Stops[0].Sent);
  }

  // A confirmation asks for its own truck's summary again, and no other.
  [Fact]
  public async Task AConfirmationRefreshesOnlyItsOwnTruck()
  {
    await using var f = await PlanningRefreshFixture.CreateAsync();
    var (truck, dispatch) = await SeedAsync(f);
    var summaries = f.Services.GetRequiredService<PlanningSummaryCache>();
    var company = f.Services.GetRequiredService<ICurrentCompany>().Id!.Value;
    var mine = new PlanningSummaryCache.Key(company, truck);
    var theirs = new PlanningSummaryCache.Key(company, Guid.NewGuid());
    foreach (var key in new[] { mine, theirs })
    {
      summaries.Keep(key, "s");
      summaries.Complete(summaries.Take()!, "s", new(key.Truck, null, null, null, null) { CalculatedAt = f.Time.GetUtcNow() });
    }
    Assert.Null(summaries.Take());

    var before = Guid.NewGuid();
    await Records(f).RecordAsync(
      Snapshot(truck, dispatch, before, revision: 3),
      [(Visit(dispatch, before, fill: true), "t")],
      "manual",
      "u1",
      default
    );

    Assert.Equal(mine, summaries.Take()!.Key);
    Assert.Null(summaries.Take());
  }

  // A hand-over that is not committed is not announced: the truck's
  // summary is not asked for again, and nothing reads as sent.
  [Fact]
  public async Task AHandOverThatFailsToCommitAsksForNothing()
  {
    var probe = new SaveFailureProbe();
    await using var f = await PlanningRefreshFixture.CreateAsync(services =>
      services.ConfigureDbContext<Infrastructure.Persistence.AppDbContext>(
        options => options.AddInterceptors(probe)
      )
    );
    var (truck, dispatch) = await SeedAsync(f);
    var summaries = f.Services.GetRequiredService<PlanningSummaryCache>();
    var company = f.Services.GetRequiredService<ICurrentCompany>().Id!.Value;
    var key = new PlanningSummaryCache.Key(company, truck);
    summaries.Keep(key, "s");
    summaries.Complete(
      summaries.Take()!,
      "s",
      new(truck, null, null, null, null) { CalculatedAt = f.Time.GetUtcNow() }
    );
    var before = Guid.NewGuid();
    probe.FailNextSave = true;

    Assert.Equal(
      0,
      await Records(f).RecordAsync(
        Snapshot(truck, dispatch, before, revision: 3),
        [(Visit(dispatch, before, fill: true), "t")],
        "manual",
        "u1",
        default
      )
    );

    Assert.Null(summaries.Take());
    f.Db.ChangeTracker.Clear();
    Assert.Empty(await f.Db.FuelVisitSends.ToListAsync());
  }

  private static FuelIssueRecords Records(PlanningRefreshFixture f) =>
    new(
      f.Db,
      f.Services.GetRequiredService<PlanningSummaryCache>(),
      f.Services.GetRequiredService<ICurrentCompany>(),
      Options.Create(new FuelIssueOptions()),
      f.Time
    );

  private static async Task<(Guid Truck, Guid Dispatch)> SeedAsync(
    PlanningRefreshFixture f
  )
  {
    var truck = new Domain.Entities.Fleet.Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = "t",
      UnitNumber = "54777",
      IsActive = true,
    };
    var load = new Load
    {
      Id = Guid.NewGuid(),
      LoadNumber = 1441,
      Status = "in_transit",
      TruckId = truck.Id,
    };
    f.Db.Trucks.Add(truck);
    f.Db.Dispatches.Add(load);
    await f.Db.SaveChangesAsync();
    return (truck.Id, load.Id);
  }

  private static TruckFuelPlanSnapshot Snapshot(
    Guid truck,
    Guid dispatch,
    Guid before,
    long revision
  ) =>
    new(
      truck,
      dispatch,
      new DateTime(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc),
      new FuelPlan(),
      [
        new(dispatch, new PlanStop(before, "Delivery", "", 1, new(40, -80)), 100)
        {
          ExecutionLegId = null,
          AssignmentRevision = revision,
        },
      ],
      null
    )
    {
      AssignmentRevision = revision,
    };

  private static FuelPlanStop Visit(
    Guid dispatch,
    Guid before,
    bool fill = false,
    double gallons = 0
  ) =>
    new()
    {
      StationId = new Guid("11111111-1111-1111-1111-111111111111"),
      DispatchId = dispatch,
      BeforeStopId = before,
      FillToTarget = fill,
      BuyGallons = gallons,
      ArrivalGallons = 60,
    };

  private static FuelPlan Plan(FuelPlanStop stop) => new() { Stops = [stop] };
}
