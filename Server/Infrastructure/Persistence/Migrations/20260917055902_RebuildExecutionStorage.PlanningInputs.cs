using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

public partial class RebuildExecutionStorage
{
  private static readonly string[] PlanningInputTables =
  [
    "DispatchBaseRoutes",
    "DispatchDeadheads",
    "DispatchRouteChoices",
    "DispatchRoutePlans",
    "DispatchSourceLinks",
    "DispatchStops",
    "Dispatches",
    "Drivers",
    "ExecutionLegs",
    "ExecutionLegStops",
    "FleetPlanningSettings",
    "LoadExecutionLegs",
    "SwitchParticipants",
    "SynchronizationCheckpoints",
    "Trailers",
    "TruckPlanningProfiles",
    "Trucks",
  ];

  private static void InstallPlanningInputRevisions(MigrationBuilder builder)
  {
    builder.Sql(
      """
      INSERT INTO "PlanningInputRevisions" ("TruckId", "Revision")
      VALUES ('00000000-0000-0000-0000-000000000000', 0);
      INSERT INTO "PlanningInputRevisions" ("TruckId", "Revision")
      SELECT "Id", 0 FROM "Trucks" ON CONFLICT DO NOTHING;

      CREATE INDEX "IX_Trucks_PlanningNumber" ON "Trucks"
        (upper(btrim("UnitNumber")));

      CREATE FUNCTION pulsr_planning_input_changed() RETURNS trigger
      LANGUAGE plpgsql AS $planning$
      DECLARE
        previous_row jsonb := '{}';
        current_row jsonb := '{}';
        rows_json jsonb;
        load_ids uuid[];
        leg_ids uuid[];
        truck_ids uuid[];
        target uuid;
        global_change boolean;
        global_id uuid := '00000000-0000-0000-0000-000000000000';
      BEGIN
        IF TG_OP <> 'INSERT' THEN previous_row := to_jsonb(OLD); END IF;
        IF TG_OP <> 'DELETE' THEN current_row := to_jsonb(NEW); END IF;
        IF TG_OP = 'UPDATE' AND previous_row = current_row THEN
          RETURN NULL;
        END IF;
        IF TG_TABLE_NAME = 'SynchronizationCheckpoints'
          AND COALESCE(current_row->>'Id', previous_row->>'Id')
            <> '5ea9c4aa-c4d8-4d6a-bb82-e0be7a6ab86c' THEN
          RETURN NULL;
        END IF;
        global_change := TG_TABLE_NAME IN (
          'Drivers', 'Trailers', 'FleetPlanningSettings',
          'SynchronizationCheckpoints'
        ) OR (
          TG_TABLE_NAME = 'Trucks' AND (
            TG_OP <> 'UPDATE' OR previous_row->>'UnitNumber'
              IS DISTINCT FROM current_row->>'UnitNumber'
          )
        );
        IF global_change THEN
          UPDATE "PlanningInputRevisions" SET "Revision" = "Revision" + 1
          WHERE "TruckId" = global_id;
        ELSE
          PERFORM 1 FROM "PlanningInputRevisions"
          WHERE "TruckId" = global_id FOR SHARE;
        END IF;
        IF TG_TABLE_NAME IN (
          'Drivers', 'Trailers', 'FleetPlanningSettings',
          'SynchronizationCheckpoints'
        ) THEN
          RETURN NULL;
        END IF;

        rows_json := jsonb_build_array(previous_row, current_row);
        SELECT array_agg(DISTINCT candidate.value::uuid) INTO leg_ids
        FROM jsonb_array_elements(rows_json) row_value
        CROSS JOIN LATERAL (VALUES
          (row_value->>'ExecutionLegId'),
          (row_value->>'PreviousExecutionLegId'),
          (row_value->>'OutgoingLegId'),
          (row_value->>'IncomingLegId'),
          (CASE WHEN TG_TABLE_NAME = 'ExecutionLegs'
            THEN row_value->>'Id' END)
        ) candidate(value) WHERE candidate.value IS NOT NULL;
        SELECT array_agg(DISTINCT id) INTO load_ids FROM (
          SELECT candidate.value::uuid AS id
          FROM jsonb_array_elements(rows_json) row_value
          CROSS JOIN LATERAL (VALUES
            (row_value->>'DispatchId'),
            (row_value->>'PreviousDispatchId'),
            (CASE WHEN TG_TABLE_NAME = 'Dispatches'
              THEN row_value->>'Id' END)
          ) candidate(value) WHERE candidate.value IS NOT NULL
          UNION
          SELECT "DispatchId" FROM "LoadExecutionLegs"
          WHERE "ExecutionLegId" = ANY(leg_ids)
        ) affected;
        SELECT array_agg(DISTINCT id ORDER BY id) INTO truck_ids FROM (
          SELECT candidate.value::uuid AS id
          FROM jsonb_array_elements(rows_json) row_value
          CROSS JOIN LATERAL (VALUES
            (row_value->>'TruckId'),
            (row_value->>'PlanningTruckId'),
            (CASE WHEN TG_TABLE_NAME = 'Trucks'
              THEN row_value->>'Id' END)
          ) candidate(value) WHERE candidate.value IS NOT NULL
          UNION
          SELECT truck."Id" FROM "Trucks" truck
          WHERE EXISTS (
            SELECT 1 FROM jsonb_array_elements(rows_json) row_value
            WHERE upper(btrim(row_value->>'TruckNumber'))
              = upper(btrim(truck."UnitNumber"))
          )
          UNION
          SELECT leg."TruckId" FROM "ExecutionLegs" leg
          WHERE leg."Id" = ANY(leg_ids) OR EXISTS (
            SELECT 1 FROM "LoadExecutionLegs" link
            WHERE link."ExecutionLegId" = leg."Id"
              AND link."DispatchId" = ANY(load_ids)
          )
          UNION
          SELECT truck."Id" FROM "Trucks" truck
          JOIN "Dispatches" load ON load."Id" = ANY(load_ids)
          WHERE truck."Id" IN (load."TruckId", load."PlanningTruckId")
            OR upper(btrim(truck."UnitNumber"))
              = upper(btrim(load."TruckNumber"))
          UNION
          SELECT truck."Id" FROM "Trucks" truck
          JOIN "DispatchStops" stop ON stop."DispatchId" = ANY(load_ids)
          WHERE truck."Id" = stop."TruckId"
            OR upper(btrim(truck."UnitNumber"))
              = upper(btrim(stop."TruckNumber"))
        ) affected WHERE id IS NOT NULL AND id <> global_id;
        FOREACH target IN ARRAY COALESCE(truck_ids, ARRAY[]::uuid[]) LOOP
          INSERT INTO "PlanningInputRevisions" ("TruckId", "Revision")
          VALUES (target, 1) ON CONFLICT ("TruckId") DO UPDATE
          SET "Revision" = "PlanningInputRevisions"."Revision" + 1;
        END LOOP;
        RETURN NULL;
      END;
      $planning$;
      """
    );
    foreach (var table in PlanningInputTables)
      builder.Sql(
        $"""
        CREATE TRIGGER pulsr_planning_inputs
        AFTER INSERT OR UPDATE OR DELETE ON "{table}"
        FOR EACH ROW EXECUTE FUNCTION pulsr_planning_input_changed();
        """
      );
  }

  private static void RemovePlanningInputRevisions(MigrationBuilder builder)
  {
    foreach (var table in PlanningInputTables)
      builder.Sql($"DROP TRIGGER pulsr_planning_inputs ON \"{table}\";");
    builder.Sql("DROP FUNCTION pulsr_planning_input_changed();");
    builder.Sql("DROP INDEX \"IX_Trucks_PlanningNumber\";");
  }
}
