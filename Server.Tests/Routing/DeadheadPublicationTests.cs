using Application.Features.Routing.Services.Deadheads;
using Application.Features.Routing.Services.Routes;
using Domain.Rules;
using Microsoft.EntityFrameworkCore;
using Server.Tests.Support;

namespace Server.Tests.Routing;

public sealed partial class DeadheadGeometryRepairTests
{
  [Theory]
  [InlineData("endpoint", 1)]
  [InlineData("endpoint", 2)]
  [InlineData("price", 1)]
  [InlineData("price", 2)]
  [InlineData("cancel", 2)]
  [InlineData("insert", 2)]
  [InlineData("unknown", 2)]
  public async Task ChangedHistoryCannotPublishStaleMileageOrRates(
    string change,
    int boundary
  )
  {
    await using var f = await Fixture.CreateAsync(null);
    var expected = (
      await f.Services.DeadheadHistory.ReadAsync([f.Load.Id], default)
    )[f.Load.Id];
    Assert.True(
      await f.Services.DeadheadHistory.MatchesAsync(expected, default)
    );
    f.Publication.BeforeBegin = async () =>
    {
      if (f.Publication.Calls != boundary)
        return;
      Assert.Null(f.Db.Database.CurrentTransaction);
      await using var competing = f.CreateContext();
      var previous = await competing
        .Dispatches.Include(x => x.Stops)
        .SingleAsync(x => x.Id != f.Load.Id);
      if (change == "endpoint")
        previous.Stops.Single(x => x.Sequence == 2).Longitude = -78;
      else if (change == "price")
        (await competing.Dispatches.SingleAsync(x => x.Id == f.Load.Id)).Price =
          2000;
      else if (change == "cancel")
        previous.Status = "cancelled";
      else
      {
        var inserted = HistoricalWorkFixture.Copy(previous);
        inserted.Id = Guid.NewGuid();
        inserted.LoadNumber = 3;
        foreach (var stop in inserted.Stops)
        {
          stop.Id = Guid.NewGuid();
          stop.DispatchId = inserted.Id;
          stop.ScheduledDate =
            change == "unknown" ? null : new DateOnly(2026, 9, 3);
        }
        competing.Dispatches.Add(inserted);
      }
      await competing.SaveChangesAsync();
    };

    await Assert.ThrowsAsync<RoutePlanningException>(
      () => f.Services.Deadheads.EnsureAsync(f.Load, f.Profile, default)
    );

    Assert.Null(f.Db.Database.CurrentTransaction);
    Assert.Equal(boundary - 1, f.Router.Calls);
    var saved = await f.Db.DispatchDeadheads.AsNoTracking().SingleAsync();
    Assert.Equal(80m, saved.Miles);
    Assert.Null(saved.RouteJson);
    var rates = await f.Db.DispatchRates.AsNoTracking().SingleOrDefaultAsync();
    if (boundary == 1)
      Assert.Null(rates);
    else
    {
      Assert.NotNull(rates);
      Assert.Equal(1000m, rates.Price);
      Assert.Equal(80m, rates.EmptyMiles);
      Assert.True(saved.RetryAfter > DateTime.UtcNow);
    }
    Assert.False(
      await f.Services.DeadheadHistory.MatchesAsync(expected, default)
    );
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task FailedPublicationRollsBackRouteAndFinancialMileage(
    bool atCommit
  )
  {
    var commit = new PublicationCommitFailureProbe();
    var rates = new DeadheadRatesFailureProbe();
    await using var f = await Fixture.CreateAsync(null, commit, rates);
    f.Publication.BeforeBegin = () =>
    {
      if (f.Publication.Calls == 2)
      {
        commit.FailNextCommit = atCommit;
        rates.FailRatesWrite = !atCommit;
      }
      return Task.CompletedTask;
    };

    if (atCommit)
      await Assert.ThrowsAsync<InvalidOperationException>(
        () => f.Services.Deadheads.EnsureAsync(f.Load, f.Profile, default)
      );
    else
      await Assert.ThrowsAsync<DbUpdateException>(
        () => f.Services.Deadheads.EnsureAsync(f.Load, f.Profile, default)
      );

    Assert.Null(f.Db.Database.CurrentTransaction);
    Assert.Equal(1, f.Router.Calls);
    if (!atCommit)
      Assert.Equal(1, rates.RouteWritesBeforeFailure);
    var saved = await f.Db.DispatchDeadheads.AsNoTracking().SingleAsync();
    var financial = await f.Db.DispatchRates.AsNoTracking().SingleAsync();
    Assert.Equal(80m, saved.Miles);
    Assert.Null(saved.RouteJson);
    Assert.True(saved.RetryAfter > DateTime.UtcNow);
    Assert.Equal(80m, financial.EmptyMiles);
    Assert.Equal(2.083333m, financial.TotalRatePerMile);
    rates.FailRatesWrite = false;
    await f.Db.SaveChangesAsync();
    await f.Services.Deadheads.EnsureAsync(f.Load, f.Profile, default);
    Assert.Equal(1, f.Router.Calls);
    Assert.Equal(
      80m,
      (await f.Db.DispatchDeadheads.AsNoTracking().SingleAsync()).Miles
    );
    Assert.Equal(
      80m,
      (await f.Db.DispatchRates.AsNoTracking().SingleAsync()).EmptyMiles
    );
  }

  [Fact]
  public async Task ProviderRunsOutsidePublicationAndCommitsMatchingRates()
  {
    await using var f = await Fixture.CreateAsync(null);
    f.Router.Read = (points, _) =>
    {
      Assert.Null(f.Db.Database.CurrentTransaction);
      return Task.FromResult(Complete(points));
    };

    await f.Services.Deadheads.EnsureAsync(f.Load, f.Profile, default);

    Assert.Null(f.Db.Database.CurrentTransaction);
    Assert.Equal(2, f.Publication.Calls);
    var saved = await f.Db.DispatchDeadheads.AsNoTracking().SingleAsync();
    var financial = await f.Db.DispatchRates.AsNoTracking().SingleAsync();
    Assert.Equal(100m, saved.Miles);
    Assert.NotNull(saved.RouteJson);
    Assert.Equal(saved.Miles, financial.EmptyMiles);
    Assert.Equal(saved.InputHash, financial.ConnectionHash);
    Assert.Equal(2m, financial.TotalRatePerMile);
  }

  [Fact]
  public async Task FreshHistoricalValidationRejectsAnOuterSnapshot()
  {
    await using var f = await Fixture.CreateAsync(null);
    var snapshot = (
      await f.Services.DeadheadHistory.ReadAsync([f.Load.Id], default)
    )[f.Load.Id];
    await using var outer = await f.Db.Database.BeginTransactionAsync();

    await Assert.ThrowsAsync<InvalidOperationException>(
      () => f.Services.DeadheadHistory.MatchesAsync(snapshot, default)
    );

    Assert.Same(outer, f.Db.Database.CurrentTransaction);
  }
}
