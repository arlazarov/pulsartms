using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Options;
using Application.Features.Routing.Services.Routes;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Server.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Integration")]
public sealed class RouteRecalculationBudgetReservationTests
{
  [Fact]
  public async Task DisabledBudgetDoesNotAccessAnUnconfiguredDatabase()
  {
    await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().Options);
    using var gates = new Application.Caching.ProcessGates();
    var budget = new RouteRecalculationBudget(db, Options.Create(new RouteRecalculationBudgetOptions { Enabled = false }), gates);
    await budget.ReserveAsync(Guid.NewGuid(), new(40, -80), default);
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();
    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => budget.ReserveAsync(Guid.NewGuid(), new(40, -80), cancellation.Token));
  }

  [Fact]
  public async Task ReservationSurvivesNewContextAndTemporaryDisableWithoutProviderSuccess()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
    var truck = Guid.NewGuid();
    using var gates = new Application.Caching.ProcessGates();
    await using (var db = new AppDbContext(options))
    {
      await db.Database.EnsureCreatedAsync();
      await new RouteRecalculationBudget(db, Options.Create(new RouteRecalculationBudgetOptions()), gates)
        .ReserveAsync(truck, new(40, -80), default);
    }
    await using var reloaded = new AppDbContext(options);
    await new RouteRecalculationBudget(reloaded, Options.Create(new RouteRecalculationBudgetOptions { Enabled = false }), gates)
      .ReserveAsync(truck, new(41, -80), default);
    Assert.Equal(1, await reloaded.RouteRecalculationAttempts.CountAsync());
    await Assert.ThrowsAsync<RoutePlanningException>(() => new RouteRecalculationBudget(reloaded,
      Options.Create(new RouteRecalculationBudgetOptions()), gates).ReserveAsync(truck, new(41, -80), default));
    Assert.Equal(1, await reloaded.RouteRecalculationAttempts.CountAsync());
  }
}
