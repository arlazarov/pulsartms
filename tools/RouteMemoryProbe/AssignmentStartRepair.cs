using System.Data;
using System.Text.Json;
using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services.Routes;
using Domain.Entities.Dispatch;
using Npgsql;

internal static class AssignmentStartRepair
{
  private static readonly Guid DispatchId = Guid.Parse(
    "360b616c-aae1-4a9f-acf2-58f82f72eb34"
  );
  private static readonly Guid TruckId = Guid.Parse(
    "341731b5-9440-43a3-adf1-ed7668c4d155"
  );
  private static readonly Guid PreviousId = Guid.Parse(
    "da63c6f0-c232-41eb-9d23-b66f3868ad12"
  );
  private static readonly Guid PickupId = Guid.Parse(
    "5dc8564b-f6b1-4dac-8f75-7ac2bc83cefa"
  );

  public static async Task RunAsync(NpgsqlConnection connection, bool apply)
  {
    await using var transaction = await connection.BeginTransactionAsync(
      IsolationLevel.Serializable
    );
    await using (
      var settings = new NpgsqlCommand(
        (apply ? "" : "SET TRANSACTION READ ONLY; ")
          + "SET LOCAL statement_timeout = '10s'; SET LOCAL lock_timeout = '3s'",
        connection,
        transaction
      )
    )
      await settings.ExecuteNonQueryAsync();
    await using var query = new NpgsqlCommand(
      """
      SELECT d."PlanningAssignmentRevision", p."PlanJson",
        (SELECT jsonb_agg(jsonb_build_object('id',s."Id",'sequence',s."Sequence",'job',s."Job",
          'latitude',s."Latitude",'longitude',s."Longitude",'truckId',s."TruckId",'truckNumber',s."TruckNumber",
          'operationRevision',s."OperationRevision",'manualAction',s."ManualAction",'manualStateAfter',s."ManualStateAfter",
          'manualCompletionRevision',s."ManualCompletionRevision",'manualCompletedAt',s."ManualCompletedAt",
          'pickedUpAt',s."PickedUpAt",'deliveredAt',s."DeliveredAt",'departedAt',s."DepartedAt") ORDER BY s."Sequence")
          FROM "DispatchStops" s WHERE s."DispatchId"=d."Id")
      FROM "Dispatches" d JOIN "DispatchRoutePlans" p ON p."DispatchId"=d."Id"
      WHERE d."Id"=@id AND d."LoadNumber"=1376 AND d."Status"='in_transit'
        AND d."PlanningTruckId"=@truck AND d."PlanningFromStopId"=@previous
        AND (d."TruckId" IS NULL OR d."TruckId"=@truck) AND (d."TruckNumber"='' OR d."TruckNumber"='54777')
        AND p."TruckId"=@truck AND octet_length(p."PlanJson")<=16777216
      """,
      connection,
      transaction
    );
    query.Parameters.AddWithValue("id", DispatchId);
    query.Parameters.AddWithValue("truck", TruckId);
    query.Parameters.AddWithValue("previous", PreviousId);
    long revision;
    await using (var row = await query.ExecuteReaderAsync())
    {
      if (!await row.ReadAsync())
        throw new InvalidOperationException(
          "Assignment no longer matches the diagnosed incident; nothing changed."
        );
      revision = row.GetInt64(0);
      if (revision != 1)
        throw new InvalidOperationException(
          "Assignment revision changed since preflight; nothing changed."
        );
      var plan = JsonSerializer.Deserialize<RoutePlan>(
        row.GetString(1),
        RoutePlanningService.Json
      )!;
      var stops = JsonSerializer.Deserialize<List<DispatchStop>>(
        row.GetString(2),
        RoutePlanningService.Json
      )!;
      if (
        plan.Version != 8
        || plan.Stops.Count != 2
        || plan.Stops[0].Id != PreviousId
        || stops.Count != 2
        || stops[0].Id != PickupId
        || stops[0].Job != "Pick Up"
        || stops[1].Id != PreviousId
        || stops[1].Job != "Drop Off"
        || stops.Any(s =>
          s.IsCompleted
          || s.ManualCompletionRevision != 0
          || s.OperationRevision != 0
          || s.ManualAction is not null
          || s.ManualStateAfter is not null
          || s.TruckId.HasValue && s.TruckId != TruckId
          || !string.IsNullOrEmpty(s.TruckNumber) && s.TruckNumber != "54777"
        )
        || stops.Any(s => !s.Latitude.HasValue || !s.Longitude.HasValue)
        || RouteGeometry.Distance(plan.Stops[0].Point, Point(stops[0])) > 5
        || RouteGeometry.Distance(plan.Stops[1].Point, Point(stops[1])) > 1
      )
        throw new InvalidOperationException(
          "Stop history no longer matches the diagnosed renumbering; nothing changed."
        );
    }
    if (apply)
    {
      await using var update = new NpgsqlCommand(
        """
        UPDATE "Dispatches" SET "PlanningFromStopId"=@pickup, "PlanningAssignmentRevision"="PlanningAssignmentRevision"+1
        WHERE "Id"=@id AND "PlanningFromStopId"=@previous AND "PlanningAssignmentRevision"=@revision
        """,
        connection,
        transaction
      );
      update.Parameters.AddWithValue("pickup", PickupId);
      update.Parameters.AddWithValue("id", DispatchId);
      update.Parameters.AddWithValue("previous", PreviousId);
      update.Parameters.AddWithValue("revision", revision);
      if (await update.ExecuteNonQueryAsync() != 1)
        throw new InvalidOperationException(
          "Concurrent assignment change; repair rolled back."
        );
      await transaction.CommitAsync();
    }
    else
      await transaction.RollbackAsync();
    Console.WriteLine(
      JsonSerializer.Serialize(
        new
        {
          operation = "restore-original-start-1376",
          applied = apply,
          dispatchId = DispatchId,
          previousStart = PreviousId,
          restoredStart = PickupId,
          previousRevision = revision,
          revision = apply ? revision + 1 : revision,
          stopStatusesChanged = false,
        }
      )
    );
  }

  private static RoutePoint Point(DispatchStop stop) =>
    new((double)stop.Latitude!.Value, (double)stop.Longitude!.Value);
}
