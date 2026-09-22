using System.Text.Json;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
  private readonly NpgsqlDataSource source;
  private readonly List<AppDbContext> opened = [];

  private PostgresFixture(
    string schema,
    string connectionString,
    NpgsqlDataSource source
  )
  {
    this.schema = schema;
    this.connectionString = connectionString;
    this.source = source;
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
    // The name carries the time it was made, so a run that dies before it
    // can clean up is collected by the next one. This database is shared:
    // leaks here are somebody else's problem to look at, not just clutter.
    var schema = FormattableString.Invariant(
      $"t_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}_{Guid.NewGuid():n}"
    );
    var text = new NpgsqlConnectionStringBuilder(recorded)
    {
      Timeout = 10,
      CommandTimeout = 30,
      // Returning a connection to the pool discards session state, and the
      // search path is session state: the initializer below would be undone
      // the moment a connection was reused, putting the run back into the
      // shared schema for its second query onwards. Every connection here is
      // its own, so the initializer always runs.
      Pooling = false,
    }.ToString();

    await using (var connection = new NpgsqlConnection(text))
    {
      await connection.OpenAsync();
      await using var create = new NpgsqlCommand(
        $"CREATE SCHEMA \"{schema}\"",
        connection
      );
      await create.ExecuteNonQueryAsync();
      await CollectAbandonedAsync(connection);
    }

    // Npgsql will put `Search Path` in the connection string, but this server
    // ignores that startup parameter - asking it afterwards still answers
    // `"$user", public`. Setting it on the open connection does hold, so
    // every physical connection sets it as it is made. Without this the
    // tests write into the fixture database's shared schema and read each
    // other's rows, which is how this was found.
    var sourceBuilder = new NpgsqlDataSourceBuilder(text);
    sourceBuilder.UsePhysicalConnectionInitializer(
      connection =>
      {
        using var command = connection.CreateCommand();
        command.CommandText = $"SET search_path TO \"{schema}\"";
        command.ExecuteNonQuery();
      },
      async connection =>
      {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SET search_path TO \"{schema}\"";
        await command.ExecuteNonQueryAsync();
      }
    );
    var source = sourceBuilder.Build();

    var fixture = new PostgresFixture(schema, text, source);
    try
    {
      await fixture.FillAsync();
    }
    catch
    {
      // The schema already exists at this point. Without this, every failure
      // between creating it and finishing left one behind: forty-one of them
      // accumulated while this fixture was being written.
      await fixture.DisposeAsync();
      throw;
    }
    return fixture;
  }

  private async Task FillAsync()
  {
    await using var db = Connect();
    // EnsureCreated would do nothing: the database exists, and it does not
    // look at schemas. The model's own script is unqualified, so it lands
    // wherever the search path points - which is this run's schema. It goes
    // through a plain command because the script contains braces, which
    // ExecuteSqlRaw would read as parameter placeholders.
    await using var command = source.CreateCommand(
      db.Database.GenerateCreateScript()
    );
    await command.ExecuteNonQueryAsync();
  }

  public AppDbContext Connect(params IInterceptor[] interceptors)
  {
    var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(source);
    if (interceptors.Length > 0)
      options.AddInterceptors(interceptors);
    var db = new AppDbContext(options.Options);
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

  // Schemas from runs that ended more than half an hour ago - far longer
  // than any run here takes, so this never removes a live one, and short
  // enough that a leak is gone before the next working session.
  private static async Task CollectAbandonedAsync(NpgsqlConnection connection)
  {
    try
    {
      await using var stale = new NpgsqlCommand(
        """
        SELECT schema_name FROM information_schema.schemata
        WHERE schema_name ~ '^t_[0-9]+_[0-9a-f]{32}$'
          AND to_timestamp(split_part(schema_name, '_', 2)::bigint)
            < now() - interval '30 minutes'
        """,
        connection
      );
      var names = new List<string>();
      await using (var reader = await stale.ExecuteReaderAsync())
        while (await reader.ReadAsync())
          names.Add(reader.GetString(0));
      foreach (var name in names)
      {
        await using var drop = new NpgsqlCommand(
          $"DROP SCHEMA IF EXISTS \"{name}\" CASCADE",
          connection
        );
        await drop.ExecuteNonQueryAsync();
      }
    }
    catch (PostgresException)
    {
      // Collecting somebody else's leftovers is a courtesy, not this run's
      // job. A failure here must not stop the run that is about to start.
    }
  }

  public async ValueTask DisposeAsync()
  {
    foreach (var db in opened)
      await db.DisposeAsync();
    await source.DisposeAsync();
    // A drop can find a connection still holding the schema. Retrying beats
    // leaving it: the collector above would only reach it an hour later.
    for (var attempt = 0; attempt < 4; attempt++)
      try
      {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var drop = new NpgsqlCommand(
          $"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE",
          connection
        );
        // Dropping the model's tables over a network link is not quick, and
        // a timeout here is what left one schema behind on each full run.
        drop.CommandTimeout = 120;
        await drop.ExecuteNonQueryAsync();
        return;
      }
      catch (Exception)
      {
        await Task.Delay(TimeSpan.FromSeconds(1));
      }
  }
}
