using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
  /// <inheritdoc />
  public partial class RebuildExecutionStorage : Migration
  {
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.Sql(
        """
        LOCK TABLE "Dispatches", "ExecutionLegs", "ExecutionVisits",
          "SwitchParticipants" IN ACCESS EXCLUSIVE MODE;
        DO $$ BEGIN
          IF EXISTS (SELECT 1 FROM "Dispatches")
            OR EXISTS (SELECT 1 FROM "ExecutionLegs")
            OR EXISTS (SELECT 1 FROM "ExecutionVisits")
            OR EXISTS (SELECT 1 FROM "SwitchParticipants") THEN
            RAISE EXCEPTION
              'Clean operational reset required before storage rebuild';
          END IF;
        END $$;
        """
      );
      migrationBuilder.AddColumn<string>(
        name: "ActorName",
        table: "DispatchDocuments",
        type: "character varying(200)",
        maxLength: 200,
        nullable: false,
        defaultValue: ""
      );
      migrationBuilder.CreateTable(
        name: "PlanningInputRevisions",
        columns: table => new
        {
          TruckId = table.Column<Guid>(type: "uuid", nullable: false),
          Revision = table.Column<long>(type: "bigint", nullable: false),
        },
        constraints: table =>
        {
          table.PrimaryKey("PK_PlanningInputRevisions", x => x.TruckId);
        }
      );
      migrationBuilder.CreateTable(
        name: "SourceRoadRequests",
        columns: table => new
        {
          DispatchId = table.Column<Guid>(type: "uuid", nullable: false),
          TruckId = table.Column<Guid>(type: "uuid", nullable: true),
          InputSignature = table.Column<string>(
            type: "character varying(64)",
            maxLength: 64,
            nullable: false
          ),
          DemandIdentity = table.Column<string>(
            type: "character varying(64)",
            maxLength: 64,
            nullable: false
          ),
          Explicit = table.Column<bool>(type: "boolean", nullable: false),
          Priority = table.Column<int>(type: "integer", nullable: false),
          RequestedVersion = table.Column<long>(
            type: "bigint",
            nullable: false
          ),
          CompletedVersion = table.Column<long>(
            type: "bigint",
            nullable: false
          ),
          RequestedAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: false
          ),
          AvailableAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: false
          ),
          LeaseId = table.Column<Guid>(type: "uuid", nullable: true),
          LeaseUntil = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: true
          ),
          Attempts = table.Column<int>(type: "integer", nullable: false),
        },
        constraints: table =>
        {
          table.PrimaryKey("PK_SourceRoadRequests", x => x.DispatchId);
        }
      );

      migrationBuilder.CreateIndex(
        name: "IX_SourceRoadRequests_Priority_AvailableAt_LeaseUntil",
        table: "SourceRoadRequests",
        columns: new[] { "Priority", "AvailableAt", "LeaseUntil" }
      );

      migrationBuilder.CreateIndex(
        name: "IX_SourceRoadRequests_RequestedAt",
        table: "SourceRoadRequests",
        column: "RequestedAt"
      );
      migrationBuilder.CreateTable(
        name: "DispatchNumberCounters",
        columns: table => new
        {
          Id = table.Column<string>(
            type: "character varying(40)",
            maxLength: 40,
            nullable: false
          ),
          NextNumber = table.Column<long>(type: "bigint", nullable: false),
        },
        constraints: table =>
        {
          table.PrimaryKey("PK_DispatchNumberCounters", x => x.Id);
        }
      );

      migrationBuilder.CreateTable(
        name: "DispatchSourceLinks",
        columns: table => new
        {
          Provider = table.Column<string>(
            type: "character varying(100)",
            maxLength: 100,
            nullable: false
          ),
          ExternalId = table.Column<string>(
            type: "character varying(200)",
            maxLength: 200,
            nullable: false
          ),
          DisplayName = table.Column<string>(
            type: "character varying(100)",
            maxLength: 100,
            nullable: false
          ),
          DispatchId = table.Column<Guid>(type: "uuid", nullable: false),
        },
        constraints: table =>
        {
          table.PrimaryKey(
            "PK_DispatchSourceLinks",
            x => new { x.Provider, x.ExternalId }
          );
          table.ForeignKey(
            name: "FK_DispatchSourceLinks_Dispatches_DispatchId",
            column: x => x.DispatchId,
            principalTable: "Dispatches",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict
          );
        }
      );

      migrationBuilder.CreateIndex(
        name: "IX_DispatchSourceLinks_DispatchId",
        table: "DispatchSourceLinks",
        column: "DispatchId",
        unique: true
      );
      migrationBuilder.AddColumn<string>(
        name: "AssignmentProposalJson",
        table: "DispatchSourceLinks",
        type: "text",
        nullable: false,
        defaultValue: ""
      );
      migrationBuilder.AlterColumn<Guid>(
        name: "RecordedBy",
        table: "Trips",
        type: "uuid",
        nullable: true,
        oldClrType: typeof(Guid),
        oldType: "uuid"
      );

      migrationBuilder.AlterColumn<Guid>(
        name: "RecordedBy",
        table: "ExecutionLegs",
        type: "uuid",
        nullable: true,
        oldClrType: typeof(Guid),
        oldType: "uuid"
      );

      migrationBuilder.AddColumn<string>(
        name: "SourceAssignmentSignature",
        table: "ExecutionLegs",
        type: "character varying(64)",
        maxLength: 64,
        nullable: false,
        defaultValue: ""
      );

      migrationBuilder.AddColumn<string>(
        name: "AssignmentSignature",
        table: "DispatchSourceLinks",
        type: "character varying(64)",
        maxLength: 64,
        nullable: false,
        defaultValue: ""
      );

      migrationBuilder.AddColumn<string>(
        name: "ExecutionReviewReason",
        table: "DispatchSourceLinks",
        type: "character varying(1000)",
        maxLength: 1000,
        nullable: true
      );
      migrationBuilder.DropForeignKey(
        name: "FK_SwitchParticipants_ExecutionVisits_ReceiveVisitId",
        table: "SwitchParticipants"
      );

      migrationBuilder.DropForeignKey(
        name: "FK_SwitchParticipants_ExecutionVisits_ReleaseVisitId",
        table: "SwitchParticipants"
      );

      migrationBuilder.DropForeignKey(
        name: "FK_TrailerCustodyIntervals_ExecutionVisits_ReceiveVisitId",
        table: "TrailerCustodyIntervals"
      );

      migrationBuilder.DropForeignKey(
        name: "FK_TrailerCustodyIntervals_ExecutionVisits_ReleaseVisitId",
        table: "TrailerCustodyIntervals"
      );

      migrationBuilder.DropTable(name: "ExecutionVisits");

      migrationBuilder.DropIndex(
        name: "IX_TrailerCustodyIntervals_ReceiveVisitId",
        table: "TrailerCustodyIntervals"
      );

      migrationBuilder.DropIndex(
        name: "IX_TrailerCustodyIntervals_ReleaseVisitId",
        table: "TrailerCustodyIntervals"
      );

      migrationBuilder.DropIndex(
        name: "IX_SwitchParticipants_ReceiveVisitId",
        table: "SwitchParticipants"
      );

      migrationBuilder.DropIndex(
        name: "IX_SwitchParticipants_ReleaseVisitId",
        table: "SwitchParticipants"
      );

      migrationBuilder.DropColumn(name: "StopsJson", table: "ExecutionLegs");

      migrationBuilder.CreateTable(
        name: "ExecutionLegRevisions",
        columns: table => new
        {
          ExecutionLegId = table.Column<Guid>(type: "uuid", nullable: false),
          Revision = table.Column<long>(type: "bigint", nullable: false),
          TruckId = table.Column<Guid>(type: "uuid", nullable: false),
          SchemaVersion = table.Column<int>(type: "integer", nullable: false),
          RecordedAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: false
          ),
          RecordedBy = table.Column<Guid>(type: "uuid", nullable: true),
          Operation = table.Column<string>(
            type: "character varying(40)",
            maxLength: 40,
            nullable: false
          ),
          CorrelationId = table.Column<Guid>(type: "uuid", nullable: true),
          SnapshotJson = table.Column<string>(type: "text", nullable: false),
        },
        constraints: table =>
        {
          table.PrimaryKey(
            "PK_ExecutionLegRevisions",
            x => new { x.ExecutionLegId, x.Revision }
          );
          table.ForeignKey(
            name: "FK_ExecutionLegRevisions_ExecutionLegs_ExecutionLegId",
            column: x => x.ExecutionLegId,
            principalTable: "ExecutionLegs",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict
          );
        }
      );

      migrationBuilder.CreateTable(
        name: "ExecutionLegStops",
        columns: table => new
        {
          ExecutionLegId = table.Column<Guid>(type: "uuid", nullable: false),
          Id = table.Column<Guid>(type: "uuid", nullable: false),
          Position = table.Column<int>(type: "integer", nullable: false),
          DispatchId = table.Column<Guid>(type: "uuid", nullable: false),
          SourceDispatchStopId = table.Column<Guid>(
            type: "uuid",
            nullable: true
          ),
          Job = table.Column<string>(type: "text", nullable: false),
          StateAfter = table.Column<string>(type: "text", nullable: false),
          OperationRevision = table.Column<long>(
            type: "bigint",
            nullable: false
          ),
          OperationRecordedAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: true
          ),
          OperationRecordedBy = table.Column<Guid>(
            type: "uuid",
            nullable: true
          ),
          Name = table.Column<string>(type: "text", nullable: false),
          Address = table.Column<string>(type: "text", nullable: false),
          City = table.Column<string>(type: "text", nullable: false),
          Province = table.Column<string>(type: "text", nullable: false),
          Country = table.Column<string>(type: "text", nullable: false),
          ZipCode = table.Column<string>(type: "text", nullable: false),
          Latitude = table.Column<decimal>(type: "numeric", nullable: true),
          Longitude = table.Column<decimal>(type: "numeric", nullable: true),
          SourceAddressJson = table.Column<string>(
            type: "text",
            nullable: false
          ),
          AddressVerifiedAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: true
          ),
          AddressRetryAfter = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: true
          ),
          ScheduledDate = table.Column<DateOnly>(type: "date", nullable: true),
          ScheduledTime = table.Column<TimeOnly>(
            type: "time without time zone",
            nullable: true
          ),
          ScheduledDate2 = table.Column<DateOnly>(type: "date", nullable: true),
          ScheduledTime2 = table.Column<TimeOnly>(
            type: "time without time zone",
            nullable: true
          ),
          IsWindow = table.Column<bool>(type: "boolean", nullable: false),
          AppointmentTimeZoneId = table.Column<string>(
            type: "text",
            nullable: false
          ),
          DriverName = table.Column<string>(type: "text", nullable: false),
          CoDriverName = table.Column<string>(type: "text", nullable: false),
          TrailerNumber = table.Column<string>(type: "text", nullable: false),
          CarrierName = table.Column<string>(type: "text", nullable: false),
          StopNo = table.Column<string>(type: "text", nullable: false),
          Notes = table.Column<string>(type: "text", nullable: false),
          Commodity = table.Column<string>(type: "text", nullable: false),
          Weight = table.Column<decimal>(type: "numeric", nullable: true),
          WeightUnit = table.Column<string>(type: "text", nullable: false),
          Pieces = table.Column<decimal>(type: "numeric", nullable: true),
          Pallets = table.Column<decimal>(type: "numeric", nullable: true),
          Temperature = table.Column<string>(type: "text", nullable: false),
          TemperatureUnit = table.Column<string>(type: "text", nullable: false),
          ArrivedAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: true
          ),
          PickedUpAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: true
          ),
          DeliveredAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: true
          ),
          DepartedAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: true
          ),
          ManualCompletedAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: true
          ),
          CompletionOverride = table.Column<bool>(
            type: "boolean",
            nullable: true
          ),
          ExecutionCompleted = table.Column<bool>(
            type: "boolean",
            nullable: false
          ),
          ManualCompletionRecordedAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: true
          ),
          ManualCompletionRevision = table.Column<long>(
            type: "bigint",
            nullable: false
          ),
          ManualCompletedBy = table.Column<Guid>(type: "uuid", nullable: true),
          ManualCompletedByName = table.Column<string>(
            type: "text",
            nullable: true
          ),
        },
        constraints: table =>
        {
          table.PrimaryKey(
            "PK_ExecutionLegStops",
            x => new { x.ExecutionLegId, x.Id }
          );
          table.ForeignKey(
            name: "FK_ExecutionLegStops_ExecutionLegs_ExecutionLegId",
            column: x => x.ExecutionLegId,
            principalTable: "ExecutionLegs",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade
          );
        }
      );

      migrationBuilder.CreateIndex(
        name: "IX_ExecutionLegRevisions_CorrelationId",
        table: "ExecutionLegRevisions",
        column: "CorrelationId"
      );

      migrationBuilder.CreateIndex(
        name: "IX_ExecutionLegRevisions_TruckId_RecordedAt",
        table: "ExecutionLegRevisions",
        columns: new[] { "TruckId", "RecordedAt" }
      );

      migrationBuilder.CreateIndex(
        name: "IX_ExecutionLegStops_DispatchId",
        table: "ExecutionLegStops",
        column: "DispatchId"
      );

      migrationBuilder.CreateIndex(
        name: "IX_ExecutionLegStops_ExecutionLegId_Position",
        table: "ExecutionLegStops",
        columns: new[] { "ExecutionLegId", "Position" }
      );
      migrationBuilder.Sql(
        """
        CREATE FUNCTION protect_execution_history()
        RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          RAISE EXCEPTION 'Accepted execution history is immutable';
        END;
        $$;
        CREATE TRIGGER execution_history_immutable
          BEFORE UPDATE OR DELETE ON "ExecutionLegRevisions"
          FOR EACH ROW EXECUTE FUNCTION protect_execution_history();
        """
      );

      migrationBuilder.CreateTable(
        name: "PlanningRefreshRequests",
        columns: table => new
        {
          Id = table.Column<string>(
            type: "character varying(100)",
            maxLength: 100,
            nullable: false
          ),
          DispatchId = table.Column<Guid>(type: "uuid", nullable: false),
          ExecutionLegId = table.Column<Guid>(type: "uuid", nullable: true),
          AssignmentRevision = table.Column<long>(
            type: "bigint",
            nullable: false
          ),
          InputSignature = table.Column<string>(
            type: "character varying(64)",
            maxLength: 64,
            nullable: false
          ),
          RequestedVersion = table.Column<long>(
            type: "bigint",
            nullable: false
          ),
          CompletedVersion = table.Column<long>(
            type: "bigint",
            nullable: false
          ),
          RequestedAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: false
          ),
          AvailableAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: false
          ),
          LeaseId = table.Column<Guid>(type: "uuid", nullable: true),
          LeaseUntil = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: true
          ),
          Attempts = table.Column<int>(type: "integer", nullable: false),
        },
        constraints: table =>
        {
          table.PrimaryKey("PK_PlanningRefreshRequests", x => x.Id);
        }
      );

      migrationBuilder.CreateIndex(
        name: "IX_PlanningRefreshRequests_AvailableAt_LeaseUntil",
        table: "PlanningRefreshRequests",
        columns: new[] { "AvailableAt", "LeaseUntil" }
      );

      migrationBuilder.CreateIndex(
        name: "IX_PlanningRefreshRequests_RequestedAt",
        table: "PlanningRefreshRequests",
        column: "RequestedAt"
      );
      InstallPlanningInputRevisions(migrationBuilder);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.Sql(
        """
        LOCK TABLE "Dispatches", "DispatchSourceLinks",
          "DispatchNumberCounters", "ExecutionLegs", "ExecutionLegRevisions",
          "SwitchParticipants", "PlanningRefreshRequests", "SourceRoadRequests"
          IN ACCESS EXCLUSIVE MODE;
        DO $$ BEGIN
          IF EXISTS (
            SELECT 1 FROM "PlanningRefreshRequests"
            WHERE "RequestedVersion" > "CompletedVersion"
          ) THEN
            RAISE EXCEPTION
              'Pending planning requires forward repair, not downgrade';
          END IF;
          IF EXISTS (
            SELECT 1 FROM "SourceRoadRequests"
            WHERE "RequestedVersion" > "CompletedVersion"
          ) THEN
            RAISE EXCEPTION
              'Pending source roads require forward repair, not downgrade';
          END IF;
          IF EXISTS (SELECT 1 FROM "ExecutionLegs")
            OR EXISTS (SELECT 1 FROM "ExecutionLegRevisions")
            OR EXISTS (SELECT 1 FROM "SwitchParticipants") THEN
            RAISE EXCEPTION
              'Accepted execution requires forward repair, not downgrade';
          END IF;
          IF EXISTS (SELECT 1 FROM "Dispatches")
            OR EXISTS (SELECT 1 FROM "DispatchSourceLinks")
            OR EXISTS (SELECT 1 FROM "DispatchNumberCounters") THEN
            RAISE EXCEPTION
              'Native dispatch requires forward repair, not downgrade';
          END IF;
        END $$;
        DROP TRIGGER execution_history_immutable
          ON "ExecutionLegRevisions";
        DROP FUNCTION protect_execution_history();
        """
      );
      RemovePlanningInputRevisions(migrationBuilder);
      migrationBuilder.DropColumn(
        name: "ActorName",
        table: "DispatchDocuments"
      );
      migrationBuilder.DropTable(name: "PlanningInputRevisions");
      migrationBuilder.DropColumn(
        name: "SourceAssignmentSignature",
        table: "ExecutionLegs"
      );

      migrationBuilder.DropColumn(
        name: "AssignmentSignature",
        table: "DispatchSourceLinks"
      );

      migrationBuilder.DropColumn(
        name: "ExecutionReviewReason",
        table: "DispatchSourceLinks"
      );

      migrationBuilder.AlterColumn<Guid>(
        name: "RecordedBy",
        table: "Trips",
        type: "uuid",
        nullable: false,
        defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
        oldClrType: typeof(Guid),
        oldType: "uuid",
        oldNullable: true
      );

      migrationBuilder.AlterColumn<Guid>(
        name: "RecordedBy",
        table: "ExecutionLegs",
        type: "uuid",
        nullable: false,
        defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
        oldClrType: typeof(Guid),
        oldType: "uuid",
        oldNullable: true
      );
      migrationBuilder.DropTable(name: "DispatchSourceLinks");
      migrationBuilder.DropTable(name: "DispatchNumberCounters");
      migrationBuilder.DropTable(name: "PlanningRefreshRequests");
      migrationBuilder.DropTable(name: "SourceRoadRequests");
      migrationBuilder.DropTable(name: "ExecutionLegRevisions");

      migrationBuilder.DropTable(name: "ExecutionLegStops");

      migrationBuilder.AddColumn<string>(
        name: "StopsJson",
        table: "ExecutionLegs",
        type: "character varying(1048576)",
        maxLength: 1048576,
        nullable: false,
        defaultValue: ""
      );

      migrationBuilder.CreateTable(
        name: "ExecutionVisits",
        columns: table => new
        {
          Id = table.Column<Guid>(type: "uuid", nullable: false),
          ActualAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: true
          ),
          ConfirmedBy = table.Column<Guid>(type: "uuid", nullable: true),
          Latitude = table.Column<decimal>(type: "numeric", nullable: false),
          Longitude = table.Column<decimal>(type: "numeric", nullable: false),
          Operation = table.Column<string>(
            type: "character varying(30)",
            maxLength: 30,
            nullable: false
          ),
          PlannedAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: true
          ),
          Revision = table.Column<long>(type: "bigint", nullable: false),
          SiteName = table.Column<string>(
            type: "character varying(500)",
            maxLength: 500,
            nullable: false
          ),
          SourceDispatchStopId = table.Column<Guid>(
            type: "uuid",
            nullable: true
          ),
          TripId = table.Column<Guid>(type: "uuid", nullable: false),
        },
        constraints: table =>
        {
          table.PrimaryKey("PK_ExecutionVisits", x => x.Id);
          table.ForeignKey(
            name: "FK_ExecutionVisits_Trips_TripId",
            column: x => x.TripId,
            principalTable: "Trips",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict
          );
        }
      );

      migrationBuilder.CreateIndex(
        name: "IX_TrailerCustodyIntervals_ReceiveVisitId",
        table: "TrailerCustodyIntervals",
        column: "ReceiveVisitId"
      );

      migrationBuilder.CreateIndex(
        name: "IX_TrailerCustodyIntervals_ReleaseVisitId",
        table: "TrailerCustodyIntervals",
        column: "ReleaseVisitId"
      );

      migrationBuilder.CreateIndex(
        name: "IX_SwitchParticipants_ReceiveVisitId",
        table: "SwitchParticipants",
        column: "ReceiveVisitId"
      );

      migrationBuilder.CreateIndex(
        name: "IX_SwitchParticipants_ReleaseVisitId",
        table: "SwitchParticipants",
        column: "ReleaseVisitId"
      );

      migrationBuilder.CreateIndex(
        name: "IX_ExecutionVisits_SourceDispatchStopId",
        table: "ExecutionVisits",
        column: "SourceDispatchStopId"
      );

      migrationBuilder.CreateIndex(
        name: "IX_ExecutionVisits_TripId",
        table: "ExecutionVisits",
        column: "TripId"
      );

      migrationBuilder.AddForeignKey(
        name: "FK_SwitchParticipants_ExecutionVisits_ReceiveVisitId",
        table: "SwitchParticipants",
        column: "ReceiveVisitId",
        principalTable: "ExecutionVisits",
        principalColumn: "Id",
        onDelete: ReferentialAction.Restrict
      );

      migrationBuilder.AddForeignKey(
        name: "FK_SwitchParticipants_ExecutionVisits_ReleaseVisitId",
        table: "SwitchParticipants",
        column: "ReleaseVisitId",
        principalTable: "ExecutionVisits",
        principalColumn: "Id",
        onDelete: ReferentialAction.Restrict
      );

      migrationBuilder.AddForeignKey(
        name: "FK_TrailerCustodyIntervals_ExecutionVisits_ReceiveVisitId",
        table: "TrailerCustodyIntervals",
        column: "ReceiveVisitId",
        principalTable: "ExecutionVisits",
        principalColumn: "Id",
        onDelete: ReferentialAction.Restrict
      );

      migrationBuilder.AddForeignKey(
        name: "FK_TrailerCustodyIntervals_ExecutionVisits_ReleaseVisitId",
        table: "TrailerCustodyIntervals",
        column: "ReleaseVisitId",
        principalTable: "ExecutionVisits",
        principalColumn: "Id",
        onDelete: ReferentialAction.Restrict
      );
    }
  }
}
