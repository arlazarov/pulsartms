using Microsoft.Extensions.Configuration;
using Npgsql;

namespace CoreMigrationProbe;

internal sealed class MigrationFixture : IAsyncDisposable
{
  private readonly string adminConnection;
  private readonly string database;

  private MigrationFixture(string adminConnection, string database)
  {
    this.adminConnection = adminConnection;
    this.database = database;
    ConnectionString = new NpgsqlConnectionStringBuilder(adminConnection)
    {
      Database = database,
    }.ConnectionString;
  }

  public string ConnectionString { get; }

  public static async Task<MigrationFixture> CreateAsync()
  {
    var configured =
      Environment.GetEnvironmentVariable("PULSR_MIGRATION_ADMIN_CONNECTION")
      ?? new ConfigurationBuilder()
        .AddUserSecrets("pulsartms-api-local")
        .Build()
        .GetConnectionString("DefaultConnection")
      ?? throw new InvalidOperationException(
        "Database credentials are missing."
      );
    var settings = new NpgsqlConnectionStringBuilder(configured)
    {
      Database = "postgres",
      IncludeErrorDetail = false,
    };
    if (
      settings.Host is { } host
      && host.EndsWith(".neon.tech", StringComparison.Ordinal)
    )
      settings.Host = host.Replace("-pooler.", ".", StringComparison.Ordinal);
    var database = $"pulsr_core_fixture_{Guid.NewGuid():N}";
    await using var connection = new NpgsqlConnection(
      settings.ConnectionString
    );
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand(
      $"CREATE DATABASE \"{database}\" TEMPLATE template0",
      connection
    );
    await command.ExecuteNonQueryAsync();
    Console.WriteLine($"Created isolated fixture: {database}");
    return new(settings.ConnectionString, database);
  }

  public async ValueTask DisposeAsync()
  {
    NpgsqlConnection.ClearAllPools();
    await using var connection = new NpgsqlConnection(adminConnection);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand(
      $"DROP DATABASE \"{database}\" WITH (FORCE)",
      connection
    );
    await command.ExecuteNonQueryAsync();
    Console.WriteLine($"Removed isolated fixture: {database}");
  }
}
