using Application.Diagnostics.Consistency;
using Application.Features.Routing.Audit;
using Application.Features.Routing.Services.Routes;
using Domain.Entities.Dispatch;
using Domain.Entities.Execution;
using Domain.Entities.Fleet;
using Domain.Models.Routing;
using Infrastructure.Integrations.GeoTimeZone;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Routing;

using Dispatch = global::Domain.Entities.Dispatch.Dispatch;

// Roads saved before the border check, AMF1414's among them, get their
// verdict from their own geometry in the background, without a provider
// call. The auditor then reports, from that verdict alone and only while
// the work can still run, a road that leaves its country as a violation
// and one that could not be placed for review.
[Trait("Category", "Routing")]
[Trait("Kind", "Integration")]
public sealed class BaseRoadBorderAuditTests
{
  private static readonly RoutePoint Ticonderoga = new(43.8486707, -73.4234531);
  private static readonly RoutePoint DePere = new(44.4488805, -88.0603806);
  private static readonly RoutePoint Toronto = new(43.6532, -79.3832);
  private static readonly RoutePoint Detroit = new(42.3314, -83.0458);
  private static readonly RoutePoint London = new(42.9849, -81.2453);
  private static readonly RoutePoint Indianapolis = new(39.7684, -86.1581);
  private static readonly RoutePoint Chicago = new(41.8781, -87.6298);

  [Fact]
  public async Task SavedRoadsAreCheckedOnceAndOnlyADomesticCrossingIsReported()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    var truck = new Truck { Id = Guid.NewGuid(), ExternalId = "border-audit" };
    db.Trucks.Add(truck);
    var incident = Saved(db, Road(Ticonderoga, [London], DePere), null);
    var crossBorder = Saved(db, Road(Toronto, [London], Detroit), null);
    var domestic = Saved(
      db,
      Road(Ticonderoga, [Indianapolis, Chicago], DePere),
      null
    );
    var unreadable = Saved(db, null, null);
    var running = Saved(db, Road(Ticonderoga, [London], DePere), Leg("active"));
    var finished = Saved(
      db,
      Road(Ticonderoga, [London], DePere),
      Leg("completed")
    );
    await db.SaveChangesAsync();
    db.ChangeTracker.Clear();

    var check = new BaseRoadBorderCheck(db, new RouteRegionLookup());
    Assert.Equal(BaseRoadBorderCheck.PageSize, await check.CheckAsync(default));
    Assert.Equal(1, await check.CheckAsync(default));
    Assert.Equal(0, await check.CheckAsync(default));

    var verdicts = await db.DispatchBaseRoutes.ToDictionaryAsync(
      x => x.Id,
      x => x.BorderCheck
    );
    Assert.Equal("CA", verdicts[incident]);
    Assert.Equal("n/a", verdicts[crossBorder]);
    Assert.Equal("stays", verdicts[domestic]);
    // Unreadable geometry is not clean: it is unknown, and reviewed.
    Assert.Equal("unknown", verdicts[unreadable]);
    Assert.Equal("CA", verdicts[running]);
    Assert.Equal("CA", verdicts[finished]);

    var company = await db
      .DispatchBaseRoutes.Select(x => x.CompanyId)
      .FirstAsync();
    var page = await new BaseRoadBorderRule(db).ReadAsync(
      new(company, DateTime.UtcNow, null, 10, TimeSpan.FromMinutes(30)),
      default
    );
    Assert.Equal(
      new[] { incident, running }.Select(x => x.ToString()).Order(),
      page.Observed.Select(x => x.EntityKey).Order()
    );
    Assert.All(page.Observed, x => Assert.Equal("CA", x.Evidence["border"]));
    Assert.False(page.More);
    var unknown = await new BaseRoadBorderUnknownRule(db).ReadAsync(
      new(company, DateTime.UtcNow, null, 10, TimeSpan.FromMinutes(30)),
      default
    );
    Assert.Equal(
      unreadable.ToString(),
      Assert.Single(unknown.Observed).EntityKey
    );

    ExecutionLeg Leg(string status) =>
      new()
      {
        Id = Guid.NewGuid(),
        Trip = new() { Id = Guid.NewGuid() },
        TruckId = truck.Id,
        Status = status,
        Revision = 1,
      };
  }

  private static Guid Saved(
    AppDbContext db,
    TruckRoute? road,
    ExecutionLeg? leg
  )
  {
    var load = new Dispatch
    {
      Id = Guid.NewGuid(),
      LoadNumber = db.ChangeTracker.Entries<Dispatch>().Count() + 1,
    };
    db.Dispatches.Add(load);
    if (leg is not null)
      db.ExecutionLegs.Add(leg);
    var saved = new DispatchBaseRoute
    {
      Id = Guid.NewGuid(),
      DispatchId = load.Id,
      ExecutionLegId = leg?.Id,
      InputHash = "saved-before-the-check",
      RouteJson = road is null ? "{not json" : RoutePlanStorage.Serialize(road),
      CalculatedAt = DateTime.UtcNow,
    };
    db.DispatchBaseRoutes.Add(saved);
    return saved.Id;
  }

  // One leg from the first point to the last, bent through the others.
  private static TruckRoute Road(
    RoutePoint from,
    RoutePoint[] via,
    RoutePoint to
  )
  {
    var leg = new RouteLeg(900, 36_000, [from, .. via, to]);
    return new()
    {
      Miles = 900,
      Seconds = 36_000,
      Legs = [leg],
      Points = [.. leg.Points],
    };
  }
}
