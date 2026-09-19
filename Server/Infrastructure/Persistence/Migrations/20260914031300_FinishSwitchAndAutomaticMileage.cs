using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
  /// <inheritdoc />
  public partial class FinishSwitchAndAutomaticMileage : Migration
  {
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.DropIndex(
        name: "IX_SwitchParticipants_IncomingLegId",
        table: "SwitchParticipants"
      );

      migrationBuilder.DropIndex(
        name: "IX_SwitchParticipants_OutgoingLegId",
        table: "SwitchParticipants"
      );

      migrationBuilder.AddColumn<bool>(
        name: "IsCancelled",
        table: "SwitchParticipants",
        type: "boolean",
        nullable: false,
        defaultValue: false
      );

      migrationBuilder.AddColumn<string>(
        name: "OutgoingRestoreJson",
        table: "SwitchParticipants",
        type: "character varying(4194304)",
        maxLength: 4194304,
        nullable: false,
        defaultValue: ""
      );

      migrationBuilder.AddColumn<Guid>(
        name: "FromVisitId",
        table: "Movements",
        type: "uuid",
        nullable: true
      );

      migrationBuilder.AddColumn<string>(
        name: "Origin",
        table: "Movements",
        type: "character varying(32)",
        maxLength: 32,
        nullable: false,
        defaultValue: "manual"
      );

      migrationBuilder.AddColumn<bool>(
        name: "PlannedSuperseded",
        table: "Movements",
        type: "boolean",
        nullable: false,
        defaultValue: false
      );

      migrationBuilder.AddColumn<Guid>(
        name: "ToVisitId",
        table: "Movements",
        type: "uuid",
        nullable: true
      );

      migrationBuilder.AddColumn<decimal>(
        name: "EndOdometerMeters",
        table: "MovementDistanceEvidence",
        type: "numeric(21,3)",
        precision: 21,
        scale: 3,
        nullable: true
      );

      migrationBuilder.AddColumn<decimal>(
        name: "StartOdometerMeters",
        table: "MovementDistanceEvidence",
        type: "numeric(21,3)",
        precision: 21,
        scale: 3,
        nullable: true
      );

      migrationBuilder.AddColumn<string>(
        name: "SourceObservedSignature",
        table: "ExecutionLegs",
        type: "character varying(64)",
        maxLength: 64,
        nullable: false,
        defaultValue: ""
      );

      migrationBuilder.AddColumn<string>(
        name: "SourceReviewReason",
        table: "ExecutionLegs",
        type: "character varying(1000)",
        maxLength: 1000,
        nullable: true
      );

      migrationBuilder.CreateTable(
        name: "ExecutionPlanningChanges",
        columns: table => new
        {
          Id = table.Column<Guid>(type: "uuid", nullable: false),
          DispatchId = table.Column<Guid>(type: "uuid", nullable: false),
          TruckId = table.Column<Guid>(type: "uuid", nullable: false),
          ExecutionLegId = table.Column<Guid>(type: "uuid", nullable: false),
          AssignmentRevision = table.Column<long>(
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
          CompletedAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: true
          ),
          LeaseId = table.Column<Guid>(type: "uuid", nullable: true),
          LeaseUntil = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: true
          ),
          Attempts = table.Column<int>(type: "integer", nullable: false),
          MileageOnly = table.Column<bool>(type: "boolean", nullable: false),
        },
        constraints: table =>
        {
          table.PrimaryKey("PK_ExecutionPlanningChanges", x => x.Id);
        }
      );

      migrationBuilder.CreateTable(
        name: "ExecutionSourceReceipts",
        columns: table => new
        {
          Id = table.Column<Guid>(type: "uuid", nullable: false),
          IdempotencyKey = table.Column<Guid>(type: "uuid", nullable: false),
          ExecutionLegId = table.Column<Guid>(type: "uuid", nullable: false),
          RequestHash = table.Column<string>(
            type: "character varying(64)",
            maxLength: 64,
            nullable: false
          ),
          ResultJson = table.Column<string>(
            type: "character varying(65536)",
            maxLength: 65536,
            nullable: false
          ),
          RecordedAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: false
          ),
          RecordedBy = table.Column<Guid>(type: "uuid", nullable: false),
        },
        constraints: table =>
        {
          table.PrimaryKey("PK_ExecutionSourceReceipts", x => x.Id);
          table.ForeignKey(
            name: "FK_ExecutionSourceReceipts_ExecutionLegs_ExecutionLegId",
            column: x => x.ExecutionLegId,
            principalTable: "ExecutionLegs",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict
          );
        }
      );

      migrationBuilder.CreateTable(
        name: "MileageCaptureGaps",
        columns: table => new
        {
          Id = table.Column<Guid>(type: "uuid", nullable: false),
          TruckId = table.Column<Guid>(type: "uuid", nullable: false),
          ExecutionLegId = table.Column<Guid>(type: "uuid", nullable: true),
          StartedAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: false
          ),
          EndedAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: false
          ),
          Reason = table.Column<string>(
            type: "character varying(100)",
            maxLength: 100,
            nullable: false
          ),
          RecordedAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: false
          ),
        },
        constraints: table =>
        {
          table.PrimaryKey("PK_MileageCaptureGaps", x => x.Id);
          table.ForeignKey(
            name: "FK_MileageCaptureGaps_ExecutionLegs_ExecutionLegId",
            column: x => x.ExecutionLegId,
            principalTable: "ExecutionLegs",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict
          );
          table.ForeignKey(
            name: "FK_MileageCaptureGaps_Trucks_TruckId",
            column: x => x.TruckId,
            principalTable: "Trucks",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict
          );
        }
      );

      migrationBuilder.CreateTable(
        name: "OdometerCaptureCheckpoints",
        columns: table => new
        {
          Id = table.Column<Guid>(type: "uuid", nullable: false),
          Cursor = table.Column<string>(
            type: "character varying(4000)",
            maxLength: 4000,
            nullable: true
          ),
          Revision = table.Column<long>(type: "bigint", nullable: false),
          UpdatedAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: false
          ),
        },
        constraints: table =>
        {
          table.PrimaryKey("PK_OdometerCaptureCheckpoints", x => x.Id);
        }
      );

      migrationBuilder.CreateTable(
        name: "OdometerIntervals",
        columns: table => new
        {
          Id = table.Column<Guid>(type: "uuid", nullable: false),
          TruckId = table.Column<Guid>(type: "uuid", nullable: false),
          ExternalTruckId = table.Column<string>(
            type: "character varying(100)",
            maxLength: 100,
            nullable: false
          ),
          StartedAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: false
          ),
          EndedAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: false
          ),
          StartMeters = table.Column<decimal>(
            type: "numeric(21,3)",
            precision: 21,
            scale: 3,
            nullable: false
          ),
          EndMeters = table.Column<decimal>(
            type: "numeric(21,3)",
            precision: 21,
            scale: 3,
            nullable: false
          ),
          RecordedAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: false
          ),
          CheckedAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: true
          ),
          Status = table.Column<string>(
            type: "character varying(16)",
            maxLength: 16,
            nullable: false
          ),
          MovementId = table.Column<Guid>(type: "uuid", nullable: true),
          GapId = table.Column<Guid>(type: "uuid", nullable: true),
        },
        constraints: table =>
        {
          table.PrimaryKey("PK_OdometerIntervals", x => x.Id);
          table.ForeignKey(
            name: "FK_OdometerIntervals_Trucks_TruckId",
            column: x => x.TruckId,
            principalTable: "Trucks",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict
          );
        }
      );

      migrationBuilder.CreateTable(
        name: "OdometerPositions",
        columns: table => new
        {
          Id = table.Column<Guid>(type: "uuid", nullable: false),
          TruckId = table.Column<Guid>(type: "uuid", nullable: false),
          ExternalTruckId = table.Column<string>(
            type: "character varying(100)",
            maxLength: 100,
            nullable: false
          ),
          ObservedAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: false
          ),
          Meters = table.Column<decimal>(
            type: "numeric(21,3)",
            precision: 21,
            scale: 3,
            nullable: false
          ),
          Revision = table.Column<long>(type: "bigint", nullable: false),
        },
        constraints: table =>
        {
          table.PrimaryKey("PK_OdometerPositions", x => x.Id);
          table.ForeignKey(
            name: "FK_OdometerPositions_Trucks_TruckId",
            column: x => x.TruckId,
            principalTable: "Trucks",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict
          );
        }
      );

      migrationBuilder.CreateIndex(
        name: "IX_SwitchParticipants_IncomingLegId",
        table: "SwitchParticipants",
        column: "IncomingLegId",
        unique: true,
        filter: "NOT \"IsCancelled\""
      );

      migrationBuilder.CreateIndex(
        name: "IX_SwitchParticipants_OutgoingLegId",
        table: "SwitchParticipants",
        column: "OutgoingLegId",
        unique: true,
        filter: "NOT \"IsCancelled\""
      );

      migrationBuilder.CreateIndex(
        name: "IX_ExecutionPlanningChanges_AvailableAt_LeaseUntil",
        table: "ExecutionPlanningChanges",
        columns: new[] { "AvailableAt", "LeaseUntil" },
        filter: "\"CompletedAt\" IS NULL"
      );

      migrationBuilder.CreateIndex(
        name: "IX_ExecutionPlanningChanges_CompletedAt",
        table: "ExecutionPlanningChanges",
        column: "CompletedAt"
      );

      migrationBuilder.CreateIndex(
        name: "IX_ExecutionPlanningChanges_ExecutionLegId_AssignmentRevision_~",
        table: "ExecutionPlanningChanges",
        columns: new[] { "ExecutionLegId", "AssignmentRevision", "DispatchId" }
      );

      migrationBuilder.CreateIndex(
        name: "IX_ExecutionSourceReceipts_ExecutionLegId",
        table: "ExecutionSourceReceipts",
        column: "ExecutionLegId"
      );

      migrationBuilder.CreateIndex(
        name: "IX_ExecutionSourceReceipts_IdempotencyKey",
        table: "ExecutionSourceReceipts",
        column: "IdempotencyKey",
        unique: true
      );

      migrationBuilder.CreateIndex(
        name: "IX_MileageCaptureGaps_ExecutionLegId_EndedAt",
        table: "MileageCaptureGaps",
        columns: new[] { "ExecutionLegId", "EndedAt" }
      );

      migrationBuilder.CreateIndex(
        name: "IX_MileageCaptureGaps_TruckId_EndedAt",
        table: "MileageCaptureGaps",
        columns: new[] { "TruckId", "EndedAt" }
      );

      migrationBuilder.CreateIndex(
        name: "IX_OdometerIntervals_Status_CheckedAt_EndedAt",
        table: "OdometerIntervals",
        columns: new[] { "Status", "CheckedAt", "EndedAt" }
      );

      migrationBuilder.CreateIndex(
        name: "IX_OdometerIntervals_TruckId_StartedAt_EndedAt",
        table: "OdometerIntervals",
        columns: new[] { "TruckId", "StartedAt", "EndedAt" },
        unique: true
      );

      migrationBuilder.CreateIndex(
        name: "IX_OdometerPositions_TruckId",
        table: "OdometerPositions",
        column: "TruckId",
        unique: true
      );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.Sql(
        """
        DO $$
        BEGIN
          IF EXISTS (SELECT 1 FROM "ExecutionSourceReceipts")
            OR EXISTS (SELECT 1 FROM "ExecutionPlanningChanges")
            OR EXISTS (SELECT 1 FROM "OdometerCaptureCheckpoints")
            OR EXISTS (SELECT 1 FROM "OdometerPositions")
            OR EXISTS (SELECT 1 FROM "OdometerIntervals")
            OR EXISTS (SELECT 1 FROM "MileageCaptureGaps")
            OR EXISTS (SELECT 1 FROM "Movements"
              WHERE "Origin" <> 'manual' OR "PlannedSuperseded")
            OR EXISTS (SELECT 1 FROM "SwitchParticipants"
              WHERE "IsCancelled" OR "OutgoingRestoreJson" <> '')
            OR EXISTS (SELECT 1 FROM "ExecutionLegs"
              WHERE "SourceObservedSignature" <> ''
                OR "SourceReviewReason" IS NOT NULL)
          THEN
            RAISE EXCEPTION
              'Execution history exists; use a forward repair.';
          END IF;
        END $$;
        """
      );
      migrationBuilder.DropTable(name: "ExecutionPlanningChanges");

      migrationBuilder.DropTable(name: "ExecutionSourceReceipts");

      migrationBuilder.DropTable(name: "MileageCaptureGaps");

      migrationBuilder.DropTable(name: "OdometerCaptureCheckpoints");

      migrationBuilder.DropTable(name: "OdometerIntervals");

      migrationBuilder.DropTable(name: "OdometerPositions");

      migrationBuilder.DropIndex(
        name: "IX_SwitchParticipants_IncomingLegId",
        table: "SwitchParticipants"
      );

      migrationBuilder.DropIndex(
        name: "IX_SwitchParticipants_OutgoingLegId",
        table: "SwitchParticipants"
      );

      migrationBuilder.DropColumn(
        name: "IsCancelled",
        table: "SwitchParticipants"
      );

      migrationBuilder.DropColumn(
        name: "OutgoingRestoreJson",
        table: "SwitchParticipants"
      );

      migrationBuilder.DropColumn(name: "FromVisitId", table: "Movements");

      migrationBuilder.DropColumn(name: "Origin", table: "Movements");

      migrationBuilder.DropColumn(
        name: "PlannedSuperseded",
        table: "Movements"
      );

      migrationBuilder.DropColumn(name: "ToVisitId", table: "Movements");

      migrationBuilder.DropColumn(
        name: "EndOdometerMeters",
        table: "MovementDistanceEvidence"
      );

      migrationBuilder.DropColumn(
        name: "StartOdometerMeters",
        table: "MovementDistanceEvidence"
      );

      migrationBuilder.DropColumn(
        name: "SourceObservedSignature",
        table: "ExecutionLegs"
      );

      migrationBuilder.DropColumn(
        name: "SourceReviewReason",
        table: "ExecutionLegs"
      );

      migrationBuilder.CreateIndex(
        name: "IX_SwitchParticipants_IncomingLegId",
        table: "SwitchParticipants",
        column: "IncomingLegId",
        unique: true
      );

      migrationBuilder.CreateIndex(
        name: "IX_SwitchParticipants_OutgoingLegId",
        table: "SwitchParticipants",
        column: "OutgoingLegId",
        unique: true
      );
    }
  }
}
