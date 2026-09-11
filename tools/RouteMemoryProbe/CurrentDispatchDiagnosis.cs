using System.Text.Json;
using Npgsql;

internal static class CurrentDispatchDiagnosis
{
  public static async Task RunAsync(NpgsqlConnection connection)
  {
    await using var transaction = await connection.BeginTransactionAsync();
    await using (var settings = new NpgsqlCommand("SET TRANSACTION READ ONLY; SET LOCAL statement_timeout = '10s'", connection, transaction))
      await settings.ExecuteNonQueryAsync();
    await using var command = new NpgsqlCommand("""
      SELECT d."Id", d."LoadNumber", d."Status", d."ShipDate", d."DeliveryDate",
        s."Sequence", s."Job", s."ScheduledDate", s."PickedUpAt", s."DeliveredAt", s."DepartedAt",
        p."PlanJson"::jsonb->'tracking' AS tracking,
        e."ForecastJson"::jsonb->'calculatedAt' AS eta_calculated, e."ForecastJson"::jsonb->'validUntil' AS eta_valid_until
      FROM "Dispatches" d LEFT JOIN "DispatchStops" s ON s."DispatchId"=d."Id"
      LEFT JOIN "DispatchRoutePlans" p ON p."DispatchId"=d."Id"
      LEFT JOIN "DispatchEtaForecasts" e ON e."DispatchId"=d."Id"
      WHERE d."TruckId"=@truck AND d."LoadNumber" IN (1375,1373)
      ORDER BY d."LoadNumber", s."Sequence" LIMIT 20
      """, connection, transaction);
    command.Parameters.AddWithValue("truck", Guid.Parse("341731b5-9440-43a3-adf1-ed7668c4d155"));
    await using var rows = await command.ExecuteReaderAsync();
    while (await rows.ReadAsync())
      Console.WriteLine(JsonSerializer.Serialize(Enumerable.Range(0, rows.FieldCount)
        .ToDictionary(rows.GetName, index => rows.IsDBNull(index) ? null : rows.GetValue(index))));
    await rows.CloseAsync();
    await transaction.RollbackAsync();
  }
}
