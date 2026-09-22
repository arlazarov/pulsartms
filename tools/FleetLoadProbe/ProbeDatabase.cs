using System.Text.RegularExpressions;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FleetLoadProbe;

internal sealed class ProbeDatabase : IAsyncDisposable
{
  public NpgsqlDataSource Source { get; }
  private readonly string connection;
  private readonly string schema;

  public ProbeDatabase(string schema)
  {
    if (!Regex.IsMatch(schema, "^load_[0-9a-f]{32}$"))
      throw new ArgumentException("Use a unique load fixture schema.");
    this.schema = schema;
    var settings = new NpgsqlConnectionStringBuilder(
      Environment.GetEnvironmentVariable("PULSR_LOAD_CONNECTION")
        ?? throw new InvalidOperationException("Fixture connection required.")
    );
    if (
      settings.Database?.StartsWith(
        "pulsr_core_fixture_",
        StringComparison.Ordinal
      ) != true
      || settings.Username != "pulsr_test_runner"
    )
      throw new InvalidOperationException(
        "Only the isolated fixture is allowed."
      );
    settings.IncludeErrorDetail = false;
    settings.Timeout = 15;
    settings.CommandTimeout = 60;
    settings.MaxPoolSize = 16;
    settings.NoResetOnClose = true;
    connection = settings.ConnectionString;
    var builder = new NpgsqlDataSourceBuilder(connection);
    // This pool belongs exclusively to one fixture schema. The remote
    // endpoint ignores the search-path startup parameter.
    builder.UsePhysicalConnectionInitializer(
      c =>
      {
        using var command = c.CreateCommand();
        command.CommandText = $"SET search_path TO \"{schema}\"";
        command.ExecuteNonQuery();
      },
      async c =>
      {
        await using var command = c.CreateCommand();
        command.CommandText = $"SET search_path TO \"{schema}\"";
        await command.ExecuteNonQueryAsync();
      }
    );
    Source = builder.Build();
  }

  public async Task VerifyAsync()
  {
    await using var command = Source.CreateCommand(
      "SELECT current_database(), current_user, current_schema()"
    );
    await using var reader = await command.ExecuteReaderAsync();
    await reader.ReadAsync();
    if (
      !reader.GetString(0).StartsWith("pulsr_core_fixture_")
      || reader.GetString(1) != "pulsr_test_runner"
      || reader.IsDBNull(2)
      || reader.GetString(2) != schema
    )
      throw new InvalidOperationException(
        "Fixture isolation verification failed."
      );
  }

  public async Task CreateAsync()
  {
    await using var c = new NpgsqlConnection(connection);
    await c.OpenAsync();
    await using var create = new NpgsqlCommand(
      $"CREATE SCHEMA \"{schema}\"",
      c
    );
    await create.ExecuteNonQueryAsync();
    await VerifyAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(Source).Options
    );
    await db.Database.MigrateAsync();
  }

  public async Task DropAsync()
  {
    await VerifyAsync();
    await Source.DisposeAsync();
    await using var c = new NpgsqlConnection(connection);
    await c.OpenAsync();
    await using var command = new NpgsqlCommand(
      $"DROP SCHEMA \"{schema}\" CASCADE",
      c
    );
    await command.ExecuteNonQueryAsync();
  }

  public ValueTask DisposeAsync() => Source.DisposeAsync();
}
