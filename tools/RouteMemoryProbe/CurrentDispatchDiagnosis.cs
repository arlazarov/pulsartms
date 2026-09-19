using System.Text.Json;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services.Routes;
using Domain.Entities.Dispatch;
using Npgsql;

internal static class CurrentDispatchDiagnosis
{
  public static async Task RouteInputsAsync(
    NpgsqlConnection connection,
    Guid dispatchId
  )
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
    await using var command = new NpgsqlCommand(
      """
      SELECT jsonb_build_object('id',d."Id",'loadNumber',d."LoadNumber",'truckId',d."TruckId",'truckNumber',d."TruckNumber",
        'planningTruckId',d."PlanningTruckId",'planningFromStopId',d."PlanningFromStopId",'routeChoiceRevision',d."RouteChoiceRevision",
        'stops',(SELECT jsonb_agg(jsonb_build_object('id',s."Id",'sequence',s."Sequence",'job',s."Job",'truckId',s."TruckId",
          'truckNumber',s."TruckNumber",'manualAction',s."ManualAction",'manualStateAfter',s."ManualStateAfter",
          'manualCompletionRevision',s."ManualCompletionRevision",'latitude',s."Latitude",'longitude',s."Longitude",
          'address',s."Address",'city',s."City",'province',s."Province",'country',s."Country",'zipCode',s."ZipCode") ORDER BY s."Sequence")
          FROM "DispatchStops" s WHERE s."DispatchId"=d."Id")),
        p."InputHash",p."TruckId",p."PlanJson",profile."SettingsJson"
      FROM "Dispatches" d JOIN "DispatchRoutePlans" p ON p."DispatchId"=d."Id"
      LEFT JOIN "TruckPlanningProfiles" profile ON profile."TruckId"=coalesce(d."PlanningTruckId",d."TruckId")
      WHERE d."Id"=@id AND octet_length(p."PlanJson")<=16777216
      """,
      connection,
      transaction
    );
    command.Parameters.AddWithValue("id", dispatchId);
    await using var row = await command.ExecuteReaderAsync();
    if (await row.ReadAsync())
    {
      var json = RoutePlanningService.Json;
      var load = JsonSerializer.Deserialize<Dispatch>(row.GetString(0), json)!;
      var plan = JsonSerializer.Deserialize<RoutePlan>(row.GetString(3), json)!;
      var profile = row.IsDBNull(4)
        ? new TruckRouteProfile()
        : JsonSerializer.Deserialize<TruckRouteProfile>(
          row.GetString(4),
          json
        )!;
      profile.TrailerLengthFeet = TruckRouteProfile.StandardTrailerFeet;
      profile.LengthFeet = profile.TrailerLengthFeet + 19;
      var itinerary = load.TruckItinerary();
      var expected = RoutePlanningService.HashInputs(itinerary, profile);
      var expectedWithSavedProfile = RoutePlanningService.HashInputs(
        itinerary,
        plan.Profile
      );
      Console.WriteLine(
        JsonSerializer.Serialize(
          new
          {
            load.LoadNumber,
            load.PlanningFromStopId,
            load.RouteChoiceRevision,
            resolvedTruck = itinerary.TruckId,
            storedTruck = row.GetGuid(2),
            plan.TruckId,
            inputsMatch = expected == row.GetString(1),
            inputsMatchWithSavedProfile = expectedWithSavedProfile
              == row.GetString(1),
            plan.Version,
            plan.CalculatedAt,
            plan.FromCurrentPosition,
            plan.OriginalPlannedMiles,
            start = plan.Route.Legs.FirstOrDefault()?.Points.FirstOrDefault(),
            legMiles = plan.Route.Legs.Select(leg => leg.Miles),
            referenceMiles = plan.ReferenceRoute?.Miles,
            referenceStart = plan
              .ReferenceRoute?.Legs.FirstOrDefault()
              ?.Points.FirstOrDefault(),
            plan.InputsChanged,
            plan.Tracking,
            miles = plan.Route.Miles,
            points = plan.Route.Points.Count,
            legPoints = plan.Route.Legs.Select(x => x.Points.Count),
            currentStops = itinerary.Stops.Select(x => new
            {
              x.Id,
              x.Sequence,
              x.Job,
              x.TruckId,
              x.Latitude,
              x.Longitude,
            }),
            savedStops = plan.Stops.Select(x => new
            {
              x.Id,
              x.Sequence,
              x.Job,
              x.Point,
            }),
            currentProfile = new
            {
              profile.HeightFeet,
              profile.WidthFeet,
              profile.LengthFeet,
              profile.WeightPounds,
              profile.Axles,
            },
            savedProfile = new
            {
              plan.Profile.HeightFeet,
              plan.Profile.WidthFeet,
              plan.Profile.LengthFeet,
              plan.Profile.WeightPounds,
              plan.Profile.Axles,
            },
          }
        )
      );
    }
    else
      Console.WriteLine("No bounded saved route found.");
    await row.CloseAsync();
    await transaction.RollbackAsync();
  }

  public static async Task TruckRoutesAsync(
    NpgsqlConnection connection,
    string unit
  )
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
    await using var command = new NpgsqlCommand(
      """
      WITH loads AS (
        SELECT d.* FROM "Dispatches" d
        WHERE d."TruckNumber"=@unit OR d."TruckId" IN (SELECT "Id" FROM "Trucks" WHERE "UnitNumber"=@unit)
          OR d."PlanningTruckId" IN (SELECT "Id" FROM "Trucks" WHERE "UnitNumber"=@unit)
          OR EXISTS (SELECT 1 FROM "DispatchStops" s WHERE s."DispatchId"=d."Id" AND
            (s."TruckNumber"=@unit OR s."TruckId" IN (SELECT "Id" FROM "Trucks" WHERE "UnitNumber"=@unit)))
        ORDER BY d."LoadNumber" DESC LIMIT 6
      )
      SELECT d."Id", d."LoadNumber", d."Status", d."PlanningFromStopId",
        s."Id" AS stop_id, s."Sequence", s."Job", s."TruckNumber", s."ManualAction", s."ManualStateAfter",
        s."ManualCompletionRevision", s."PickedUpAt", s."DeliveredAt", s."DepartedAt", s."ManualCompletedAt",
        s."Latitude", s."Longitude",
        (SELECT jsonb_agg(jsonb_build_object(
          'legId', e."Id", 'truckId', e."TruckId", 'status', e."Status",
          'revision', e."Revision", 'startedAt', e."StartedAt",
          'completedAt', e."CompletedAt", 'sequence', link."Sequence"))
          FROM "LoadExecutionLegs" link
          JOIN "ExecutionLegs" e ON e."Id"=link."ExecutionLegId"
          WHERE link."DispatchId"=d."Id") AS executions,
        p."PlanJson"::jsonb->'version' AS route_version,
        jsonb_array_length(p."PlanJson"::jsonb->'stops') AS saved_stop_count,
        p."PlanJson"::jsonb->'fromCurrentPosition' AS from_current
      FROM loads d LEFT JOIN "DispatchStops" s ON s."DispatchId"=d."Id"
      LEFT JOIN "DispatchRoutePlans" p ON p."DispatchId"=d."Id"
      ORDER BY d."LoadNumber" DESC, s."Sequence" LIMIT 150
      """,
      connection,
      transaction
    );
    command.Parameters.AddWithValue("unit", unit);
    await using var rows = await command.ExecuteReaderAsync();
    while (await rows.ReadAsync())
      Console.WriteLine(
        JsonSerializer.Serialize(
          Enumerable
            .Range(0, rows.FieldCount)
            .ToDictionary(
              rows.GetName,
              index => rows.IsDBNull(index) ? null : rows.GetValue(index)
            )
        )
      );
    await rows.CloseAsync();
    await transaction.RollbackAsync();
  }

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
    await using var command = new NpgsqlCommand(
      """
      SELECT d."Id", d."LoadNumber", d."Status", d."ShipDate", d."DeliveryDate",
        s."Sequence", s."Job", s."ScheduledDate", s."PickedUpAt", s."DeliveredAt", s."DepartedAt",
        p."PlanJson"::jsonb->'tracking' AS tracking,
        e."ForecastJson"::jsonb->'calculatedAt' AS eta_calculated, e."ForecastJson"::jsonb->'validUntil' AS eta_valid_until
      FROM "Dispatches" d LEFT JOIN "DispatchStops" s ON s."DispatchId"=d."Id"
      LEFT JOIN "DispatchRoutePlans" p ON p."DispatchId"=d."Id"
      LEFT JOIN "DispatchEtaForecasts" e ON e."DispatchId"=d."Id"
      WHERE d."TruckId"=@truck AND d."LoadNumber" IN (1375,1373)
      ORDER BY d."LoadNumber", s."Sequence" LIMIT 20
      """,
      connection,
      transaction
    );
    command.Parameters.AddWithValue(
      "truck",
      Guid.Parse("341731b5-9440-43a3-adf1-ed7668c4d155")
    );
    await using var rows = await command.ExecuteReaderAsync();
    while (await rows.ReadAsync())
      Console.WriteLine(
        JsonSerializer.Serialize(
          Enumerable
            .Range(0, rows.FieldCount)
            .ToDictionary(
              rows.GetName,
              index => rows.IsDBNull(index) ? null : rows.GetValue(index)
            )
        )
      );
    await rows.CloseAsync();
    await transaction.RollbackAsync();
  }
}
