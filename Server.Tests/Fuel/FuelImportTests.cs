using Application.Features.Fuel.Commands.ImportFuelDiscounts;
using Application.Features.Fuel.Interfaces;
using Application.Features.Fuel.Models;
using Application.Features.Fuel.Services;
using Domain.Entities.Fuel;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Server.Tests.Support;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Integration")]
public class FuelImportTests
{
  [Fact]
  public async Task NewStationAndBothAttachmentsAreSavedOnce()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    using var reads = TestCache.Create();
    var handler = new ImportFuelDiscountsHandler(
      db,
      new Provider([Import("CAD"), Import("USD")]),
      new FuelStationLookupService(
        new MemoryFuelStationLookupStore(),
        new Places(),
        TimeProvider.System
      ),
      reads,
      TimeProvider.System,
      new(),
      NullLogger<ImportFuelDiscountsHandler>.Instance
    );
    Assert.True((await handler.Handle(new(), default)).Success);
    Assert.Equal(1, await db.FuelStations.CountAsync());
    Assert.Equal(2, await db.FuelDiscounts.CountAsync());
    Assert.Equal(1, await db.FuelImportSources.CountAsync());
    Assert.All(
      await db.FuelDiscounts.ToListAsync(),
      x =>
      {
        Assert.Equal(.2m, x.Savings);
        Assert.Equal("Diesel", x.Product);
      }
    );
    Assert.Equal(0, (await handler.Handle(new(), default)).Response);
    Assert.Equal(2, await db.FuelDiscounts.CountAsync());
  }

  [Fact]
  public async Task FailureInSecondAttachmentRollsBackTheWholeMessage()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    var options = new DbContextOptionsBuilder<AppDbContext>()
      .UseSqlite(connection)
      .Options;
    await using (var db = new AppDbContext(options))
    {
      await db.Database.EnsureCreatedAsync();
      using var reads = TestCache.Create();
      var second = Import("USD");
      second.Rows[0].StationId = "second";
      second.Rows[0].Name = "Fail";
      var handler = new ImportFuelDiscountsHandler(
        db,
        new Provider([Import("CAD"), second]),
        new FuelStationLookupService(
          new MemoryFuelStationLookupStore(),
          new Places(),
          TimeProvider.System
        ),
        reads,
        TimeProvider.System,
        new(),
        NullLogger<ImportFuelDiscountsHandler>.Instance
      );
      await Assert.ThrowsAsync<HttpRequestException>(
        () => handler.Handle(new(), default)
      );
    }
    await using var verify = new AppDbContext(options);
    Assert.Equal(0, await verify.FuelStations.CountAsync());
    Assert.Equal(0, await verify.FuelDiscounts.CountAsync());
    Assert.Equal(0, await verify.FuelImportSources.CountAsync());
  }

  // Audit F20: an empty attachment threw for the whole run, so every later
  // push failed on the same message and newer prices waited behind it until
  // it left the two-day window. Only its own message is skipped now.
  [Fact]
  public async Task AnEmptyMessageDoesNotStopTheNextOne()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    using var reads = TestCache.Create();
    var empty = Import("CAD");
    empty.MessageId = "empty";
    empty.Rows.Clear();
    var handler = new ImportFuelDiscountsHandler(
      db,
      new Provider([empty, Import("USD")]),
      new FuelStationLookupService(
        new MemoryFuelStationLookupStore(),
        new Places(),
        TimeProvider.System
      ),
      reads,
      TimeProvider.System,
      new(),
      NullLogger<ImportFuelDiscountsHandler>.Instance
    );

    var result = await handler.Handle(new(), default);

    Assert.True(result.Success);
    Assert.Equal(
      ["message"],
      await db.FuelImportSources.Select(x => x.GmailMessageId).ToListAsync()
    );
    Assert.Equal(1, await db.FuelDiscounts.CountAsync());
  }

  // An attachment that could not be read skips its message, is reported
  // once however often the mailbox shows it again, and is not marked
  // imported: a corrected parser still imports it.
  [Fact]
  public async Task AnUnreadableMessageIsSkippedAndReportedOnce()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    using var reads = TestCache.Create();
    var unreadable = Import("CAD");
    unreadable.MessageId = "unreadable";
    unreadable.Rows.Clear();
    unreadable.Unreadable = true;
    var log = new Warnings();
    var skips = new FuelImportSkips();
    ImportFuelDiscountsHandler Handler() =>
      new(
        db,
        new Provider([unreadable, Import("USD")]),
        new FuelStationLookupService(
          new MemoryFuelStationLookupStore(),
          new Places(),
          TimeProvider.System
        ),
        reads,
        TimeProvider.System,
        skips,
        log
      );

    Assert.True((await Handler().Handle(new(), default)).Success);
    Assert.True((await Handler().Handle(new(), default)).Success);

    Assert.Equal(
      ["message"],
      await db.FuelImportSources.Select(x => x.GmailMessageId).ToListAsync()
    );
    Assert.Equal(
      ["Fuel import skipped message unreadable: unreadable"],
      log.Lines
    );
  }

  // The mailbox is read from two days before the last import, so a longer
  // outage loses no message; never less than two days back nor more than
  // thirty.
  [Fact]
  public async Task TheMailboxIsReadFromTheLastImport()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    using var reads = TestCache.Create();
    var time = new ManualTimeProvider(
      new DateTimeOffset(2026, 9, 27, 21, 0, 0, TimeSpan.Zero)
    );
    var now = time.GetUtcNow().UtcDateTime;
    var provider = new Provider([]);
    var handler = new ImportFuelDiscountsHandler(
      db,
      provider,
      new FuelStationLookupService(
        new MemoryFuelStationLookupStore(),
        new Places(),
        TimeProvider.System
      ),
      reads,
      time,
      new(),
      NullLogger<ImportFuelDiscountsHandler>.Instance
    );
    async Task<DateTime> SinceAfter(DateTime? imported)
    {
      await db.FuelImportSources.ExecuteDeleteAsync();
      if (imported is { } at)
      {
        db.FuelImportSources.Add(
          new FuelImportSource
          {
            Id = Guid.NewGuid(),
            GmailMessageId = at.Ticks.ToString(),
            ImportedAt = at,
          }
        );
        await db.SaveChangesAsync();
      }
      await handler.Handle(new(), default);
      return provider.Since[^1];
    }

    Assert.Equal(now.AddDays(-2), await SinceAfter(null));
    Assert.Equal(
      now.AddDays(-2).AddHours(-3),
      await SinceAfter(now.AddHours(-3))
    );
    Assert.Equal(now.AddDays(-7), await SinceAfter(now.AddDays(-5)));
    Assert.Equal(now.AddDays(-30), await SinceAfter(now.AddDays(-60)));
  }

  private static FuelDiscountImportData Import(string currency) =>
    new()
    {
      MessageId = "message",
      AttachmentName = currency + ".csv",
      Currency = currency,
      EffectiveDate = new DateOnly(2026, 9, 5),
      Rows =
      [
        new()
        {
          StationId = "station",
          Name = "Station",
          State = "ON",
          City = "Toronto",
          RetailPrice = 2m,
          DiscountPrice = 1.8m,
        },
      ],
    };

  private sealed class Provider(IReadOnlyList<FuelDiscountImportData> imports)
    : IFuelDiscountProvider
  {
    public Task<IReadOnlyList<FuelDiscountImportData>> GetDiscountsAsync(
      IReadOnlyCollection<string> ids,
      DateTime since,
      CancellationToken ct = default
    )
    {
      Since.Add(since);
      return Task.FromResult(imports);
    }

    public List<DateTime> Since { get; } = [];
  }

  private sealed class Places : IPlaceSearchService
  {
    // Reading by id is not what these exercise; they place stations by name.
    public Task<PlaceSearchResult?> ReadAsync(
      string placeId,
      CancellationToken cancellationToken = default
    ) => Task.FromResult<PlaceSearchResult?>(null);

    public Task<PlaceSearchResult?> SearchAsync(
      string query,
      CancellationToken ct = default
    ) =>
      query.StartsWith("Fail")
        ? throw new HttpRequestException("Test failure")
        : Task.FromResult<PlaceSearchResult?>(
          new()
          {
            Address = "Address",
            Latitude = 43,
            Longitude = -79,
          }
        );
  }

  private sealed class Warnings : ILogger<ImportFuelDiscountsHandler>
  {
    public List<string> Lines { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
      where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
      LogLevel logLevel,
      EventId eventId,
      TState state,
      Exception? exception,
      Func<TState, Exception?, string> formatter
    )
    {
      if (logLevel == LogLevel.Warning)
        Lines.Add(formatter(state, exception));
    }
  }
}
