using System.Text.Json;
using Application;
using Application.Features.Fleet.Interfaces;
using Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

internal static class TelemetryProviderDiagnosis
{
  public static async Task RunAsync()
  {
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
    var ct = timeout.Token;
    var config = new ConfigurationBuilder()
      .SetBasePath(Path.GetFullPath("Server/API"))
      .AddJsonFile("appsettings.json")
      .AddJsonFile("appsettings.Development.json")
      .AddUserSecrets("pulsartms-api-local")
      .AddEnvironmentVariables()
      .Build();
    await using var connection = new NpgsqlConnection(
      config.GetConnectionString("DefaultConnection")
    );
    await connection.OpenAsync(ct);
    await using var transaction = await connection.BeginTransactionAsync(ct);
    await using (
      var settings = new NpgsqlCommand(
        "SET TRANSACTION READ ONLY; SET LOCAL statement_timeout='10s'",
        connection,
        transaction
      )
    )
      await settings.ExecuteNonQueryAsync(ct);
    var fleet = new Dictionary<string, string>();
    await using (
      var query = new NpgsqlCommand(
        """
        SELECT "ExternalId", "UnitNumber" FROM "Trucks"
        WHERE "IsActive" = true AND "ExternalId" IS NOT NULL LIMIT 100
        """,
        connection,
        transaction
      )
    )
    await using (var rows = await query.ExecuteReaderAsync(ct))
      while (await rows.ReadAsync(ct))
        fleet[rows.GetString(0)] = rows.GetString(1);
    var services = new ServiceCollection();
    services.AddSingleton<IConfiguration>(config);
    services.AddLogging();
    services.AddApplication();
    services.AddInfrastructure(config);
    await using var provider = services.BuildServiceProvider();
    await using var scope = provider.CreateAsyncScope();
    var telemetry =
      scope.ServiceProvider.GetRequiredService<IFleetTelemetryProvider>();
    var now = DateTime.UtcNow;
    var snapshot = await telemetry.GetVehicleTelemetryAsync(ct);
    var stream = await telemetry.GetLocationStreamAsync(
      fleet.Keys.ToArray(),
      now.AddMinutes(-1),
      now,
      null,
      ct
    );
    foreach (var (id, unit) in fleet)
    {
      var vehicle = snapshot.FirstOrDefault(x => x.ExternalId == id);
      var point = stream
        .Data.Where(x => x.ExternalId == id)
        .MaxBy(x => x.UpdatedAt);
      Console.WriteLine(
        JsonSerializer.Serialize(
          new
          {
            unit,
            snapshotAt = vehicle?.UpdatedAt,
            snapshotHasAddress = !string.IsNullOrWhiteSpace(
              vehicle?.FormattedLocation
            ),
            streamAt = point?.UpdatedAt,
            streamHasAddress = !string.IsNullOrWhiteSpace(
              point?.FormattedLocation
            ),
            streamHasMorePages = stream.HasNextPage,
            checkedAt = now,
          }
        )
      );
    }
  }
}
