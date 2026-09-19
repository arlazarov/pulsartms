using System.Text.Json;
using Npgsql;

internal static class TelemetryFreshnessDiagnosis
{
  public static async Task RunAsync(NpgsqlConnection connection)
  {
    await using var transaction = await connection.BeginTransactionAsync();
    await using (
      var settings = new NpgsqlCommand(
        "SET TRANSACTION READ ONLY; SET LOCAL statement_timeout = '10s'",
        connection,
        transaction
      )
    )
      await settings.ExecuteNonQueryAsync();
    await using var query = new NpgsqlCommand(
      """
      SELECT t."UnitNumber", c."UpdatedAt",
        v.value->>'updatedAt', v.value->>'speed',
        v.value->>'engineState', v.value->>'engineUpdatedAt'
      FROM "SynchronizationCheckpoints" c,
        jsonb_each(c."StateJson"::jsonb->'vehicles') v
      JOIN "Trucks" t ON t."ExternalId" = v.key
      WHERE c."Id" = '6acb468e-c923-481f-b9c9-6363febf4c0a'
        AND t."IsActive" = true
      ORDER BY t."UnitNumber"
      LIMIT 100
      """,
      connection,
      transaction
    );
    await using var rows = await query.ExecuteReaderAsync();
    while (await rows.ReadAsync())
      Console.WriteLine(
        JsonSerializer.Serialize(
          new
          {
            unit = rows.GetString(0),
            checkpointAt = rows.GetDateTime(1),
            gpsAt = rows.IsDBNull(2) ? null : rows.GetString(2),
            speed = rows.IsDBNull(3) ? null : rows.GetString(3),
            engine = rows.IsDBNull(4) ? null : rows.GetString(4),
            engineAt = rows.IsDBNull(5) ? null : rows.GetString(5),
            checkedAt = DateTime.UtcNow,
          }
        )
      );
    await rows.DisposeAsync();
    await using var jobs = new NpgsqlCommand(
      """
      SELECT j.key, j.value->>'lastSuccess', j.value->>'nextRun',
        j.value->>'failures', j.value->>'error'
      FROM "SynchronizationCheckpoints" c,
        jsonb_each(c."StateJson"::jsonb->'jobs') j
      WHERE c."Id" = '6acb468e-c923-481f-b9c9-6363febf4c0a'
      ORDER BY j.key LIMIT 100
      """,
      connection,
      transaction
    );
    await using var jobRows = await jobs.ExecuteReaderAsync();
    while (await jobRows.ReadAsync())
      Console.WriteLine(
        JsonSerializer.Serialize(
          new
          {
            job = jobRows.GetString(0),
            lastSuccess = jobRows.IsDBNull(1) ? null : jobRows.GetString(1),
            nextRun = jobRows.IsDBNull(2) ? null : jobRows.GetString(2),
            failures = jobRows.IsDBNull(3) ? null : jobRows.GetString(3),
            errorType = jobRows.IsDBNull(4) ? null : jobRows.GetString(4),
          }
        )
      );
    await jobRows.DisposeAsync();
    await using var forecasts = new NpgsqlCommand(
      """
      SELECT t."UnitNumber", f."CalculatedAt", f."ValidUntil",
        s->'hours'->>'cycleVerified',
        s->'hours'->>'cycleAtArrivalMinutes',
        s->'hours'->>'currentCycleMinutes'
      FROM "DispatchEtaForecasts" f
      JOIN "Trucks" t ON t."Id" = f."TruckId",
        jsonb_array_elements(f."ForecastJson"::jsonb->'stops') s
      WHERE t."IsActive" = true AND f."DispatchId" = f."RootDispatchId"
      ORDER BY t."UnitNumber", f."CalculatedAt" DESC LIMIT 20
      """,
      connection,
      transaction
    );
    await using var forecastRows = await forecasts.ExecuteReaderAsync();
    while (await forecastRows.ReadAsync())
      Console.WriteLine(
        JsonSerializer.Serialize(
          new
          {
            unit = forecastRows.GetString(0),
            forecastAt = forecastRows.GetDateTime(1),
            forecastUntil = forecastRows.GetDateTime(2),
            cycleVerified = forecastRows.IsDBNull(3)
              ? null
              : forecastRows.GetString(3),
            arrivalCycle = forecastRows.IsDBNull(4)
              ? null
              : forecastRows.GetString(4),
            currentCycle = forecastRows.IsDBNull(5)
              ? null
              : forecastRows.GetString(5),
          }
        )
      );
  }
}
