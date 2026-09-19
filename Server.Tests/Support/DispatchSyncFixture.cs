using System.Data.Common;
using Application.Caching;
using Application.Features.Dispatch.Commands.SyncDispatche;
using Application.Features.Dispatch.Interfaces;
using Application.Features.Dispatch.Models;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;

namespace Server.Tests.Support;

internal sealed class DispatchSyncFixture : IAsyncDisposable
{
  private readonly SqliteConnection connection = new("Data Source=:memory:");
  public AppDbContext Db { get; private set; } = null!;
  public ReadCache Reads { get; } = TestCache.Create();
  public MemoryCache Memory { get; } = new(new MemoryCacheOptions());
  public List<ExternalDispatch> Sources { get; } = [];
  public SqlCounter Counter { get; } = new();
  public SyncDispatchesCommandHandler Handler =>
    new(
      Db,
      [new Provider(Sources)],
      DispatchImportTestData.Options,
      Reads,
      Memory,
      TestCache.Preparation()
    );

  public static async Task<DispatchSyncFixture> CreateAsync()
  {
    var fixture = new DispatchSyncFixture();
    await fixture.connection.OpenAsync();
    fixture.Db = new(
      new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlite(fixture.connection)
        .AddInterceptors(fixture.Counter)
        .Options
    );
    await fixture.Db.Database.EnsureCreatedAsync();
    return fixture;
  }

  public sealed class SqlCounter : DbCommandInterceptor
  {
    public int Reads;
    public List<string> Sql { get; } = [];

    public override ValueTask<
      InterceptionResult<DbDataReader>
    > ReaderExecutingAsync(
      DbCommand command,
      CommandEventData eventData,
      InterceptionResult<DbDataReader> result,
      CancellationToken cancellationToken = default
    )
    {
      Reads++;
      Sql.Add(command.CommandText);
      return ValueTask.FromResult(result);
    }

    public void Reset()
    {
      Reads = 0;
      Sql.Clear();
    }
  }

  private sealed class Provider(IReadOnlyList<ExternalDispatch> sources)
    : IDispatchProvider
  {
    public string Key => DispatchImportTestData.Key;
    public string DisplayName => DispatchImportTestData.DisplayName;

    public Task<IReadOnlyList<ExternalDispatch>> GetDispatchesAsync(
      CancellationToken ct = default
    ) => Task.FromResult(DispatchImportTestData.Identify(sources));

    public Task<IReadOnlyList<ExternalDispatch>> GetDispatchesAsync(
      DateOnly from,
      DateOnly to,
      CancellationToken ct = default
    ) => Task.FromResult(DispatchImportTestData.Identify(sources));
  }

  public async ValueTask DisposeAsync()
  {
    await Db.DisposeAsync();
    await connection.DisposeAsync();
    Reads.Dispose();
    Memory.Dispose();
  }
}
