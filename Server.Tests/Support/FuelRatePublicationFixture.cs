using Application.Caching;
using Application.Features.Fuel.Models;
using Application.Features.Fuel.Services;
using Application.Features.Synchronization.Options;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Server.Tests.Support;

internal sealed class FuelRatePublicationFixture : IAsyncDisposable
{
  public required SqliteConnection Connection { get; init; }
  public required AppDbContext Db { get; init; }
  public required FuelExchangeRateStore Store { get; init; }
  public required PublicationProbe Publication { get; init; }
  public required PublicationCommitFailureProbe Failure { get; init; }
  public required ReadCache Reads { get; init; }
  public required StubFuelExchangeRateProvider Provider { get; init; }
  public required FuelExchangeRateService Service { get; init; }

  public static async Task<FuelRatePublicationFixture> CreateAsync()
  {
    var connection = new SqliteConnection(
      new SqliteConnectionStringBuilder
      {
        DataSource = "fuel-rate-" + Guid.NewGuid(),
        Mode = SqliteOpenMode.Memory,
        Cache = SqliteCacheMode.Shared,
        DefaultTimeout = 1,
      }.ToString()
    );
    await connection.OpenAsync();
    var failure = new PublicationCommitFailureProbe();
    var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlite(connection)
        .AddInterceptors(failure)
        .Options
    );
    await db.Database.EnsureCreatedAsync();
    var publication = new PublicationProbe(db);
    var store = new FuelExchangeRateStore(
      db,
      publication,
      NullLogger<FuelExchangeRateStore>.Instance
    );
    var reads = new ReadCache(Options.Create(new SynchronizationOptions()));
    var provider = new StubFuelExchangeRateProvider();
    return new()
    {
      Connection = connection,
      Db = db,
      Store = store,
      Publication = publication,
      Failure = failure,
      Reads = reads,
      Provider = provider,
      Service = new(store, provider, reads, TimeProvider.System),
    };
  }

  public async Task<FuelExchangeRate> SeedAsync()
  {
    var now = DateTime.UtcNow;
    var rate = new FuelExchangeRate(
      .71m,
      DateOnly.FromDateTime(now).AddDays(-1),
      now.AddHours(-2)
    );
    await Store.AcquireAsync("seed", now, default);
    await Store.SaveAsync("seed", rate, default);
    await Store.ReleaseAsync("seed", default);
    return rate;
  }

  public async ValueTask DisposeAsync()
  {
    Reads.Dispose();
    await Db.DisposeAsync();
    await Connection.DisposeAsync();
  }
}
