using System.Data.Common;
using Application.Diagnostics.Consistency;
using Application.Features.Routing.Audit;
using Application.Features.Routing.Services.Routes;
using Application.Interfaces;
using Domain.Entities.Dispatch;
using Domain.Entities.Execution;
using Domain.Entities.Fleet;
using Domain.Models.Routing;
using Infrastructure.Integrations.GeoTimeZone;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Server.Tests.Support;

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

  // A road saved again between the check's read and its write keeps the
  // newer verdict: the write matches only the road that was read. A road
  // re-saved without a verdict (by an older release) is judged on the
  // next pass from the geometry now saved, not the geometry first read.
  [Theory]
  [InlineData("CA")]
  [InlineData(null)]
  public async Task ARoadSavedAgainDuringTheCheckKeepsItsNewerVerdict(
    string? newer
  )
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var plain = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await plain.Database.EnsureCreatedAsync();
    var id = Saved(plain, Road(Ticonderoga, [Indianapolis, Chicago], DePere));
    await plain.SaveChangesAsync();
    var resave = new BeforeTheWrite(
      () =>
        plain
          .DispatchBaseRoutes.Where(x => x.Id == id)
          .ExecuteUpdateAsync(set =>
            set.SetProperty(
                x => x.RouteJson,
                RoutePlanStorage.Serialize(Road(Ticonderoga, [London], DePere))
              )
              .SetProperty(x => x.CalculatedAt, DateTime.UtcNow.AddMinutes(5))
              .SetProperty(x => x.BorderCheck, newer)
          )
    );
    await using var checking = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlite(connection)
        .AddInterceptors(resave)
        .Options
    );
    var check = new BaseRoadBorderCheck(checking, new RouteRegionLookup());

    Assert.Equal(1, await check.CheckAsync(default));

    Assert.True(resave.Fired);
    Assert.Equal(newer, await Verdict(plain, id));
    Assert.Equal(newer is null ? 1 : 0, await check.CheckAsync(default));
    Assert.Equal("CA", await Verdict(plain, id));
  }

  // The preparation pass checks one carrier at a time. A context serving
  // one carrier reads and writes only that carrier's roads; the other's
  // stay unchecked until its own pass.
  [Fact]
  public async Task TheCheckServesOneCarrierAtATime()
  {
    var other = Guid.NewGuid();
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var plain = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await plain.Database.EnsureCreatedAsync();
    var amf = Saved(plain, Road(Ticonderoga, [London], DePere));
    var theirs = Saved(plain, Road(Ticonderoga, [London], DePere), null, other);
    await plain.SaveChangesAsync();
    var serving = new TestCompany();
    await using var scoped = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlite(connection)
        .UseApplicationServiceProvider(
          new ServiceCollection()
            .AddSingleton<ICurrentCompany>(serving)
            .BuildServiceProvider()
        )
        .Options
    );
    var check = new BaseRoadBorderCheck(scoped, new RouteRegionLookup());

    using (serving.As(other))
      Assert.Equal(1, await check.CheckAsync(default));

    Assert.Equal("CA", await Verdict(plain, theirs));
    Assert.Null(await Verdict(plain, amf));
    Assert.Equal(1, await check.CheckAsync(default));
    Assert.Equal("CA", await Verdict(plain, amf));
  }

  private static Task<string?> Verdict(AppDbContext db, Guid id) =>
    db
      .DispatchBaseRoutes.IgnoreQueryFilters()
      .AsNoTracking()
      .Where(x => x.Id == id)
      .Select(x => x.BorderCheck)
      .SingleAsync();

  // Runs `between` once, just before the check's write reaches the
  // database, as another request saving the road would.
  private sealed class BeforeTheWrite(Func<Task> between) : DbCommandInterceptor
  {
    public bool Fired { get; private set; }

    public override async ValueTask<
      InterceptionResult<int>
    > NonQueryExecutingAsync(
      DbCommand command,
      CommandEventData eventData,
      InterceptionResult<int> result,
      CancellationToken cancellationToken = default
    )
    {
      if (
        !Fired
        && command.CommandText.Contains("UPDATE")
        && command.CommandText.Contains("BorderCheck")
      )
      {
        Fired = true;
        await between();
      }
      return result;
    }
  }

  // A load and its road saved before the check, for the fixture's carrier
  // unless another is named.
  private static Guid Saved(
    AppDbContext db,
    TruckRoute? road,
    ExecutionLeg? leg = null,
    Guid company = default
  )
  {
    var load = new Dispatch
    {
      Id = Guid.NewGuid(),
      CompanyId = company,
      LoadNumber = db.ChangeTracker.Entries<Dispatch>().Count() + 1,
    };
    db.Dispatches.Add(load);
    if (leg is not null)
      db.ExecutionLegs.Add(leg);
    var saved = new DispatchBaseRoute
    {
      Id = Guid.NewGuid(),
      CompanyId = company,
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
