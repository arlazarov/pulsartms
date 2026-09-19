using System.Net.Http.Headers;
using System.Text.Json;
using Application;
using Application.Features.Integrations.Interfaces;
using Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

internal static class TelemetryFeedDiagnosis
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
    await using var settings = new NpgsqlCommand(
      "SET TRANSACTION READ ONLY; SET LOCAL statement_timeout='10s'",
      connection,
      transaction
    );
    await settings.ExecuteNonQueryAsync(ct);
    await using var query = new NpgsqlCommand(
      """
      SELECT "StateJson"::jsonb->>'telemetryCursor'
      FROM "SynchronizationCheckpoints"
      WHERE "Id" = '6acb468e-c923-481f-b9c9-6363febf4c0a'
      """,
      connection,
      transaction
    );
    var cursor = await query.ExecuteScalarAsync(ct) as string;
    var services = new ServiceCollection();
    services.AddSingleton<IConfiguration>(config);
    services.AddLogging();
    services.AddApplication();
    services.AddInfrastructure(config);
    await using var provider = services.BuildServiceProvider();
    await using var scope = provider.CreateAsyncScope();
    var credentials = await scope
      .ServiceProvider.GetRequiredService<IIntegrationCredentials>()
      .GetAsync("samsara", ct);
    using var http = new HttpClient();
    foreach (var decorated in new[] { true, false })
    foreach (var continued in new[] { true, false })
    {
      if (continued && string.IsNullOrEmpty(cursor))
        continue;
      var url =
        "https://api.samsara.com/fleet/vehicles/stats/feed"
        + "?types=gps,engineStates,fuelPercents";
      if (decorated)
        url += "&decorations=ambientAirTemperatureMilliC";
      if (continued)
        url += "&after=" + Uri.EscapeDataString(cursor!);
      using var request = new HttpRequestMessage(HttpMethod.Get, url);
      request.Headers.Authorization = new AuthenticationHeaderValue(
        "Bearer",
        credentials.Get("apiKey")
      );
      using var response = await http.SendAsync(request, ct);
      var body = response.IsSuccessStatusCode
        ? ""
        : await response.Content.ReadAsStringAsync(ct);
      var concepts = new[]
      {
        "cursor",
        "expired",
        "invalid",
        "decoration",
        "after",
        "types",
        "mismatch",
        "parameter",
        "permission",
        "token",
      };
      Console.WriteLine(
        JsonSerializer.Serialize(
          new
          {
            decorated,
            continued,
            status = (int)response.StatusCode,
            errorConcepts = concepts
              .Where(x => body.Contains(x, StringComparison.OrdinalIgnoreCase))
              .ToArray(),
          }
        )
      );
    }
  }
}
