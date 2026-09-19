using System.Text.Json;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Server.Tests.Support;

// Almost every test runs on SQLite, which is fine for logic but silent about
// everything the real engine does: advisory locks compile to nothing outside
// Npgsql, and serializable conflicts and SKIP LOCKED behave differently or not
// at all. A test that needs the engine's own behaviour uses this.
//
// It borrows the recorded development fixture database and gives each run its
// own schema, so runs do not see each other's tables. Advisory locks are
// per-database rather than per-schema, so two runs at once against the same
// database can still block each other; the lock ids here are the product's,
// not invented ones, which is the point.
public sealed class PostgresFixture : IAsyncDisposable
{
  private readonly string schema;
  private readonly string connectionString;
  private readonly List<AppDbContext> opened = [];

  private PostgresFixture(string schema, string connectionString)
  {
    this.schema = schema;
    this.connectionString = connectionString;
  }

  // Whether this machine has a development database recorded at all. A
  // machine without one skips these tests; a machine with one that does not
  // answer fails them, because that is a broken setup rather than an absent
  // one.
  public static bool IsRecorded => Recorded() is not null;

  public static async Task<PostgresFixture> CreateAsync()
  {
    var recorded =
      Recorded()
      ?? throw new InvalidOperationException(
        "No development Postgres is recorded."
      );
    var schema = "t_" + Guid.NewGuid().ToString("n");
    var builder = new NpgsqlConnectionStringBuilder(recorded)
    {
      SearchPath = schema,
      Timeout = 10,
      CommandTimeout = 30,
      // The pool would outlive the schema this run creates.
      Pooling = false,
    };
    await using (var connection = new NpgsqlConnection(builder.ToString()))
    {
      await connection.OpenAsync();
      await using var create = new NpgsqlCommand(
        $"CREATE SCHEMA \"{schema}\"",
        connection
      );
      await create.ExecuteNonQueryAsync();
    }
    var fixture = new PostgresFixture(schema, builder.ToString());
    await using var db = fixture.Connect();
    await db.Database.EnsureCreatedAsync();
    return fixture;
  }

  public AppDbContext Connect()
  {
    var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(connectionString)
        .Options
    );
    opened.Add(db);
    return db;
  }

  private static string? Recorded()
  {
    var path = Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
      ".local/share/pulsartms/development/databases.json"
    );
    if (!File.Exists(path))
      return null;
    try
    {
      using var document = JsonDocument.Parse(File.ReadAllText(path));
      var root = document.RootElement;
      var rows =
        root.ValueKind == JsonValueKind.Array ? root
        : root.TryGetProperty("databases", out var named) ? named
        : default;
      if (rows.ValueKind != JsonValueKind.Array)
        return null;
      // The fixture database, not the development one: it exists for test
      // runs and holds nothing anybody is looking at.
      foreach (var row in rows.EnumerateArray())
        if (
          row.TryGetProperty("database", out var name)
          && name.GetString() is { } text
          && text.StartsWith("pulsr_core_fixture", StringComparison.Ordinal)
          && row.TryGetProperty("connection", out var connection)
        )
          return connection.GetString();
      return null;
    }
    catch (JsonException)
    {
      return null;
    }
  }

  public async ValueTask DisposeAsync()
  {
    foreach (var db in opened)
      await db.DisposeAsync();
    try
    {
      await using var connection = new NpgsqlConnection(connectionString);
      await connection.OpenAsync();
      await using var drop = new NpgsqlCommand(
        $"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE",
        connection
      );
      await drop.ExecuteNonQueryAsync();
    }
    catch (Exception)
    {
      // The schema is named after a fresh guid, so a leak is inert. Failing
      // here would replace a real test result with a cleanup error.
    }
  }
}
