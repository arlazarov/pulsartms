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

  // Runs while the provider is being read, before the pass's transaction:
  // what happens here happened during the read.
  public Func<Task>? DuringRead { get; set; }
  public SqlCounter Counter { get; } = new();
  public SyncDispatchesCommandHandler Handler =>
    new(
      Db,
      [new Provider(Sources, () => DuringRead)],
      DispatchImportTestData.Options,
      Reads,
      Memory,
      TestCache.Preparation(),
      new TestCompany(),
      new DispatchReadTicketStore(Db)
    );

  // Another process's handler over the same database: its own provider
  // answer, read cache and load snapshots.
  public SyncDispatchesCommandHandler HandlerFor(
    AppDbContext db,
    IReadOnlyList<ExternalDispatch> sources,
    ReadCache reads,
    MemoryCache memory
  ) =>
    new(
      db,
      [new Provider(sources, () => null)],
      DispatchImportTestData.Options,
      reads,
      memory,
      TestCache.Preparation(),
      new TestCompany(),
      new DispatchReadTicketStore(db)
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

  // Another context over the same database, as a separate unit of work.
  public AppDbContext NewContext(params IInterceptor[] interceptors) =>
    new(
      new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlite(connection)
        .AddInterceptors([Counter, .. interceptors])
        .Options
    );

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

  private sealed class Provider(
    IReadOnlyList<ExternalDispatch> sources,
    Func<Func<Task>?> during
  ) : IDispatchProvider
  {
    public string Key => DispatchImportTestData.Key;
    public string DisplayName => DispatchImportTestData.DisplayName;

    public async Task<IReadOnlyList<ExternalDispatch>> GetDispatchesAsync(
      CancellationToken ct = default
    )
    {
      var read = DispatchImportTestData.Identify(sources);
      if (during() is { } hook)
        await hook();
      return read;
    }

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
