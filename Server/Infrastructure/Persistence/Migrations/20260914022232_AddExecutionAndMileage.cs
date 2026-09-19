using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
  /// <inheritdoc />
  public partial class AddExecutionAndMileage : Migration
  {
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.DropIndex(
        name: "IX_DispatchRoutePlans_DispatchId",
        table: "DispatchRoutePlans"
      );

      migrationBuilder.DropIndex(
        name: "IX_DispatchRouteChoices_DispatchId",
        table: "DispatchRouteChoices"
      );

      migrationBuilder.DropIndex(
        name: "IX_DispatchEtaForecasts_DispatchId",
        table: "DispatchEtaForecasts"
      );

      migrationBuilder.DropIndex(
        name: "IX_DispatchDeadheads_DispatchId",
        table: "DispatchDeadheads"
      );

      migrationBuilder.DropIndex(
        name: "IX_DispatchBaseRoutes_DispatchId",
        table: "DispatchBaseRoutes"
      );

      migrationBuilder.AddColumn<long>(
        name: "ConfigurationRevision",
        table: "Trucks",
        type: "bigint",
        nullable: false,
        defaultValue: 0L
      );

      migrationBuilder.AddColumn<DateTime>(
        name: "ConfiguredAt",
        table: "Trucks",
        type: "timestamp with time zone",
        nullable: true
      );

      migrationBuilder.AddColumn<string>(
        name: "ConfiguredBy",
        table: "Trucks",
        type: "character varying(200)",
        maxLength: 200,
        nullable: true
      );

      migrationBuilder.AddColumn<bool>(
        name: "ImportedIsActive",
        table: "Trucks",
        type: "boolean",
        nullable: true
      );

      migrationBuilder.AddColumn<string>(
        name: "ImportedVin",
        table: "Trucks",
        type: "character varying(17)",
        maxLength: 17,
        nullable: true
      );

      migrationBuilder.AddColumn<bool>(
        name: "IsLocallyConfigured",
        table: "Trucks",
        type: "boolean",
        nullable: false,
        defaultValue: false
      );

      migrationBuilder.AddColumn<long>(
        name: "ConfigurationRevision",
        table: "Trailers",
        type: "bigint",
        nullable: false,
        defaultValue: 0L
      );

      migrationBuilder.AddColumn<DateTime>(
        name: "ConfiguredAt",
        table: "Trailers",
        type: "timestamp with time zone",
        nullable: true
      );

      migrationBuilder.AddColumn<string>(
        name: "ConfiguredBy",
        table: "Trailers",
        type: "character varying(200)",
        maxLength: 200,
        nullable: true
      );

      migrationBuilder.AddColumn<bool>(
        name: "ImportedIsActive",
        table: "Trailers",
        type: "boolean",
        nullable: true
      );

      migrationBuilder.AddColumn<string>(
        name: "ImportedVin",
        table: "Trailers",
        type: "character varying(17)",
        maxLength: 17,
        nullable: true
      );

      migrationBuilder.AddColumn<bool>(
        name: "IsLocallyConfigured",
        table: "Trailers",
        type: "boolean",
        nullable: false,
        defaultValue: false
      );

      migrationBuilder.AddColumn<long>(
        name: "ConfigurationRevision",
        table: "Drivers",
        type: "bigint",
        nullable: false,
        defaultValue: 0L
      );

      migrationBuilder.AddColumn<DateTime>(
        name: "ConfiguredAt",
        table: "Drivers",
        type: "timestamp with time zone",
        nullable: true
      );

      migrationBuilder.AddColumn<string>(
        name: "ConfiguredBy",
        table: "Drivers",
        type: "character varying(200)",
        maxLength: 200,
        nullable: true
      );

      migrationBuilder.AddColumn<string>(
        name: "ImportedFuelCard",
        table: "Drivers",
        type: "character varying(50)",
        maxLength: 50,
        nullable: true
      );

      migrationBuilder.AddColumn<bool>(
        name: "ImportedIsActive",
        table: "Drivers",
        type: "boolean",
        nullable: true
      );

      migrationBuilder.AddColumn<string>(
        name: "ImportedName",
        table: "Drivers",
        type: "character varying(200)",
        maxLength: 200,
        nullable: true
      );

      migrationBuilder.AddColumn<bool>(
        name: "IsLocallyConfigured",
        table: "Drivers",
        type: "boolean",
        nullable: false,
        defaultValue: false
      );

      migrationBuilder.AddColumn<Guid>(
        name: "ExecutionLegId",
        table: "DispatchRoutePreviews",
        type: "uuid",
        nullable: true
      );

      migrationBuilder.AddColumn<long>(
        name: "AssignmentRevision",
        table: "DispatchRoutePlans",
        type: "bigint",
        nullable: false,
        defaultValue: 0L
      );

      migrationBuilder.AddColumn<Guid>(
        name: "ExecutionLegId",
        table: "DispatchRoutePlans",
        type: "uuid",
        nullable: true
      );

      migrationBuilder.AddColumn<Guid>(
        name: "ExecutionLegId",
        table: "DispatchRouteChoices",
        type: "uuid",
        nullable: true
      );

      migrationBuilder.AddColumn<long>(
        name: "AssignmentRevision",
        table: "DispatchEtaForecasts",
        type: "bigint",
        nullable: false,
        defaultValue: 0L
      );

      migrationBuilder.AddColumn<Guid>(
        name: "ExecutionLegId",
        table: "DispatchEtaForecasts",
        type: "uuid",
        nullable: true
      );

      migrationBuilder.AddColumn<Guid>(
        name: "RootExecutionLegId",
        table: "DispatchEtaForecasts",
        type: "uuid",
        nullable: true
      );

      migrationBuilder.AddColumn<Guid>(
        name: "ExecutionLegId",
        table: "DispatchDeadheads",
        type: "uuid",
        nullable: true
      );

      migrationBuilder.AddColumn<Guid>(
        name: "PreviousExecutionLegId",
        table: "DispatchDeadheads",
        type: "uuid",
        nullable: true
      );

      migrationBuilder.AddColumn<Guid>(
        name: "ExecutionLegId",
        table: "DispatchBaseRoutes",
        type: "uuid",
        nullable: true
      );

      migrationBuilder.CreateTable(
        name: "DispatchSwitchOperations",
        columns: table => new
        {
          Id = table.Column<Guid>(type: "uuid", nullable: false),
          Status = table.Column<string>(
            type: "character varying(20)",
            maxLength: 20,
            nullable: false
          ),
          Revision = table.Column<long>(type: "bigint", nullable: false),
          SiteName = table.Column<string>(
            type: "character varying(500)",
            maxLength: 500,
            nullable: false
          ),
          Latitude = table.Column<decimal>(type: "numeric", nullable: false),
          Longitude = table.Column<decimal>(type: "numeric", nullable: false),
          PlannedAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: true
          ),
          CompletedAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: true
          ),
          RecordedAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: false
          ),
          RecordedBy = table.Column<Guid>(type: "uuid", nullable: false),
          CompletedBy = table.Column<Guid>(type: "uuid", nullable: true),
          IdempotencyKey = table.Column<Guid>(type: "uuid", nullable: false),
          RequestHash = table.Column<string>(
            type: "character varying(64)",
            maxLength: 64,
            nullable: false
          ),
        },
        constraints: table =>
        {
          table.PrimaryKey("PK_DispatchSwitchOperations", x => x.Id);
        }
      );

      migrationBuilder.CreateTable(
        name: "MileageAllocationPolicies",
        columns: table => new
        {
          Id = table.Column<Guid>(type: "uuid", nullable: false),
          Revision = table.Column<long>(type: "bigint", nullable: false),
          YardReturn = table.Column<string>(
            type: "character varying(16)",
            maxLength: 16,
            nullable: false
          ),
          Home = table.Column<string>(
            type: "character varying(16)",
            maxLength: 16,
            nullable: false
          ),
          Maintenance = table.Column<string>(
            type: "character varying(16)",
            maxLength: 16,
            nullable: false
          ),
          Reposition = table.Column<string>(
            type: "character varying(16)",
            maxLength: 16,
            nullable: false
          ),
          UpdatedAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: true
          ),
          UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
        },
        constraints: table =>
        {
          table.PrimaryKey("PK_MileageAllocationPolicies", x => x.Id);
        }
      );

      migrationBuilder.CreateTable(
        name: "Trips",
        columns: table => new
        {
          Id = table.Column<Guid>(type: "uuid", nullable: false),
          Name = table.Column<string>(
            type: "character varying(500)",
            maxLength: 500,
            nullable: false
          ),
          Status = table.Column<string>(
            type: "character varying(20)",
            maxLength: 20,
            nullable: false
          ),
          Revision = table.Column<long>(type: "bigint", nullable: false),
          RecordedAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: false
          ),
          RecordedBy = table.Column<Guid>(type: "uuid", nullable: false),
        },
        constraints: table =>
        {
          table.PrimaryKey("PK_Trips", x => x.Id);
        }
      );

      migrationBuilder.CreateTable(
        name: "ExecutionLegs",
        columns: table => new
        {
          Id = table.Column<Guid>(type: "uuid", nullable: false),
          TripId = table.Column<Guid>(type: "uuid", nullable: false),
          TruckId = table.Column<Guid>(type: "uuid", nullable: false),
          DriverId = table.Column<Guid>(type: "uuid", nullable: true),
          CoDriverId = table.Column<Guid>(type: "uuid", nullable: true),
          TrailerId = table.Column<Guid>(type: "uuid", nullable: true),
          Status = table.Column<string>(
            type: "character varying(20)",
            maxLength: 20,
            nullable: false
          ),
          Revision = table.Column<long>(type: "bigint", nullable: false),
          RouteChoiceRevision = table.Column<long>(
            type: "bigint",
            nullable: false
          ),
          StartSwitchId = table.Column<Guid>(type: "uuid", nullable: true),
          EndSwitchId = table.Column<Guid>(type: "uuid", nullable: true),
          StartedAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: true
          ),
          CompletedAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: true
          ),
          RecordedAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: false
          ),
          RecordedBy = table.Column<Guid>(type: "uuid", nullable: false),
          StopsJson = table.Column<string>(
            type: "character varying(1048576)",
            maxLength: 1048576,
            nullable: false
          ),
          SourceSignature = table.Column<string>(
            type: "character varying(64)",
            maxLength: 64,
            nullable: false
          ),
        },
        constraints: table =>
        {
          table.PrimaryKey("PK_ExecutionLegs", x => x.Id);
          table.ForeignKey(
            name: "FK_ExecutionLegs_DispatchSwitchOperations_EndSwitchId",
            column: x => x.EndSwitchId,
            principalTable: "DispatchSwitchOperations",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict
          );
          table.ForeignKey(
            name: "FK_ExecutionLegs_DispatchSwitchOperations_StartSwitchId",
            column: x => x.StartSwitchId,
            principalTable: "DispatchSwitchOperations",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict
          );
          table.ForeignKey(
            name: "FK_ExecutionLegs_Drivers_CoDriverId",
            column: x => x.CoDriverId,
            principalTable: "Drivers",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict
          );
          table.ForeignKey(
            name: "FK_ExecutionLegs_Drivers_DriverId",
            column: x => x.DriverId,
            principalTable: "Drivers",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict
          );
          table.ForeignKey(
            name: "FK_ExecutionLegs_Trailers_TrailerId",
            column: x => x.TrailerId,
            principalTable: "Trailers",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict
          );
          table.ForeignKey(
            name: "FK_ExecutionLegs_Trips_TripId",
            column: x => x.TripId,
            principalTable: "Trips",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict
          );
          table.ForeignKey(
            name: "FK_ExecutionLegs_Trucks_TruckId",
            column: x => x.TruckId,
            principalTable: "Trucks",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict
          );
        }
      );

      migrationBuilder.CreateTable(
        name: "ExecutionVisits",
        columns: table => new
        {
          Id = table.Column<Guid>(type: "uuid", nullable: false),
          TripId = table.Column<Guid>(type: "uuid", nullable: false),
          SourceDispatchStopId = table.Column<Guid>(
            type: "uuid",
            nullable: true
          ),
          Operation = table.Column<string>(
            type: "character varying(30)",
            maxLength: 30,
            nullable: false
          ),
          SiteName = table.Column<string>(
            type: "character varying(500)",
            maxLength: 500,
            nullable: false
          ),
          Latitude = table.Column<decimal>(type: "numeric", nullable: false),
          Longitude = table.Column<decimal>(type: "numeric", nullable: false),
          PlannedAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: true
          ),
          ActualAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: true
          ),
          ConfirmedBy = table.Column<Guid>(type: "uuid", nullable: true),
          Revision = table.Column<long>(type: "bigint", nullable: false),
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

      migrationBuilder.CreateTable(
        name: "LoadExecutionLegs",
        columns: table => new
        {
          Id = table.Column<Guid>(type: "uuid", nullable: false),
          DispatchId = table.Column<Guid>(type: "uuid", nullable: false),
          ExecutionLegId = table.Column<Guid>(type: "uuid", nullable: false),
          Sequence = table.Column<int>(type: "integer", nullable: false),
          StartVisitId = table.Column<Guid>(type: "uuid", nullable: false),
          EndVisitId = table.Column<Guid>(type: "uuid", nullable: false),
        },
        constraints: table =>
        {
          table.PrimaryKey("PK_LoadExecutionLegs", x => x.Id);
          table.ForeignKey(
            name: "FK_LoadExecutionLegs_Dispatches_DispatchId",
            column: x => x.DispatchId,
            principalTable: "Dispatches",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict
          );
          table.ForeignKey(
            name: "FK_LoadExecutionLegs_ExecutionLegs_ExecutionLegId",
            column: x => x.ExecutionLegId,
            principalTable: "ExecutionLegs",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict
          );
        }
      );

      migrationBuilder.CreateTable(
        name: "Movements",
        columns: table => new
        {
          Id = table.Column<Guid>(type: "uuid", nullable: false),
          IdempotencyKey = table.Column<Guid>(type: "uuid", nullable: false),
          RequestHash = table.Column<string>(
            type: "character varying(64)",
            maxLength: 64,
            nullable: false
          ),
          TruckId = table.Column<Guid>(type: "uuid", nullable: false),
          DriverId = table.Column<Guid>(type: "uuid", nullable: true),
          CoDriverId = table.Column<Guid>(type: "uuid", nullable: true),
          TrailerId = table.Column<Guid>(type: "uuid", nullable: true),
          ExecutionLegId = table.Column<Guid>(type: "uuid", nullable: true),
          Purpose = table.Column<string>(
            type: "character varying(32)",
            maxLength: 32,
            nullable: false
          ),
          CargoState = table.Column<string>(
            type: "character varying(16)",
            maxLength: 16,
            nullable: false
          ),
          PreviousDispatchId = table.Column<Guid>(type: "uuid", nullable: true),
          NextDispatchId = table.Column<Guid>(type: "uuid", nullable: true),
          CarriedDispatchId = table.Column<Guid>(type: "uuid", nullable: true),
          FromLocation = table.Column<string>(
            type: "character varying(500)",
            maxLength: 500,
            nullable: false
          ),
          ToLocation = table.Column<string>(
            type: "character varying(500)",
            maxLength: 500,
            nullable: false
          ),
          StartedAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: true
          ),
          EndedAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: true
          ),
          RecordedAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: false
          ),
          RecordedBy = table.Column<Guid>(type: "uuid", nullable: false),
          Revision = table.Column<long>(type: "bigint", nullable: false),
          PlannedMiles = table.Column<decimal>(
            type: "numeric(18,3)",
            precision: 18,
            scale: 3,
            nullable: true
          ),
          ActualMiles = table.Column<decimal>(
            type: "numeric(18,3)",
            precision: 18,
            scale: 3,
            nullable: true
          ),
          PlannedEvidenceId = table.Column<Guid>(type: "uuid", nullable: true),
          ActualEvidenceId = table.Column<Guid>(type: "uuid", nullable: true),
          PlannedAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: true
          ),
          ActualAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: true
          ),
          PlannedSource = table.Column<string>(
            type: "character varying(64)",
            maxLength: 64,
            nullable: true
          ),
          PlannedSourceReference = table.Column<string>(
            type: "character varying(300)",
            maxLength: 300,
            nullable: true
          ),
          ActualSource = table.Column<string>(
            type: "character varying(64)",
            maxLength: 64,
            nullable: true
          ),
          ActualSourceReference = table.Column<string>(
            type: "character varying(300)",
            maxLength: 300,
            nullable: true
          ),
          AllocatedDispatchId = table.Column<Guid>(
            type: "uuid",
            nullable: true
          ),
          AllocationTarget = table.Column<string>(
            type: "character varying(16)",
            maxLength: 16,
            nullable: false
          ),
          AllocationReason = table.Column<string>(
            type: "character varying(500)",
            maxLength: 500,
            nullable: false
          ),
          PolicyRevision = table.Column<long>(type: "bigint", nullable: false),
          ManualOverride = table.Column<bool>(type: "boolean", nullable: false),
        },
        constraints: table =>
        {
          table.PrimaryKey("PK_Movements", x => x.Id);
          table.ForeignKey(
            name: "FK_Movements_Dispatches_AllocatedDispatchId",
            column: x => x.AllocatedDispatchId,
            principalTable: "Dispatches",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict
          );
          table.ForeignKey(
            name: "FK_Movements_Dispatches_CarriedDispatchId",
            column: x => x.CarriedDispatchId,
            principalTable: "Dispatches",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict
          );
          table.ForeignKey(
            name: "FK_Movements_Dispatches_NextDispatchId",
            column: x => x.NextDispatchId,
            principalTable: "Dispatches",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict
          );
          table.ForeignKey(
            name: "FK_Movements_Dispatches_PreviousDispatchId",
            column: x => x.PreviousDispatchId,
            principalTable: "Dispatches",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict
          );
          table.ForeignKey(
            name: "FK_Movements_Drivers_CoDriverId",
            column: x => x.CoDriverId,
            principalTable: "Drivers",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict
          );
          table.ForeignKey(
            name: "FK_Movements_Drivers_DriverId",
            column: x => x.DriverId,
            principalTable: "Drivers",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict
          );
          table.ForeignKey(
            name: "FK_Movements_ExecutionLegs_ExecutionLegId",
            column: x => x.ExecutionLegId,
            principalTable: "ExecutionLegs",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict
          );
          table.ForeignKey(
            name: "FK_Movements_Trailers_TrailerId",
            column: x => x.TrailerId,
            principalTable: "Trailers",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict
          );
          table.ForeignKey(
            name: "FK_Movements_Trucks_TruckId",
            column: x => x.TruckId,
            principalTable: "Trucks",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict
          );
        }
      );

      migrationBuilder.CreateTable(
        name: "SwitchParticipants",
        columns: table => new
        {
          Id = table.Column<Guid>(type: "uuid", nullable: false),
          SwitchId = table.Column<Guid>(type: "uuid", nullable: false),
          DispatchId = table.Column<Guid>(type: "uuid", nullable: false),
          OutgoingLegId = table.Column<Guid>(type: "uuid", nullable: false),
          IncomingLegId = table.Column<Guid>(type: "uuid", nullable: false),
          ReleaseVisitId = table.Column<Guid>(type: "uuid", nullable: false),
          ReceiveVisitId = table.Column<Guid>(type: "uuid", nullable: false),
          TransferKind = table.Column<string>(
            type: "character varying(30)",
            maxLength: 30,
            nullable: false
          ),
          PlannedReleaseAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: true
          ),
          PlannedReceiveAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: true
          ),
          ReleasedAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: true
          ),
          ReceivedAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: true
          ),
          ReleasedBy = table.Column<Guid>(type: "uuid", nullable: true),
          ReceivedBy = table.Column<Guid>(type: "uuid", nullable: true),
          Revision = table.Column<long>(type: "bigint", nullable: false),
        },
        constraints: table =>
        {
          table.PrimaryKey("PK_SwitchParticipants", x => x.Id);
          table.ForeignKey(
            name: "FK_SwitchParticipants_DispatchSwitchOperations_SwitchId",
            column: x => x.SwitchId,
            principalTable: "DispatchSwitchOperations",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict
          );
          table.ForeignKey(
            name: "FK_SwitchParticipants_Dispatches_DispatchId",
            column: x => x.DispatchId,
            principalTable: "Dispatches",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict
          );
          table.ForeignKey(
            name: "FK_SwitchParticipants_ExecutionLegs_IncomingLegId",
            column: x => x.IncomingLegId,
            principalTable: "ExecutionLegs",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict
          );
          table.ForeignKey(
            name: "FK_SwitchParticipants_ExecutionLegs_OutgoingLegId",
            column: x => x.OutgoingLegId,
            principalTable: "ExecutionLegs",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict
          );
          table.ForeignKey(
            name: "FK_SwitchParticipants_ExecutionVisits_ReceiveVisitId",
            column: x => x.ReceiveVisitId,
            principalTable: "ExecutionVisits",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict
          );
          table.ForeignKey(
            name: "FK_SwitchParticipants_ExecutionVisits_ReleaseVisitId",
            column: x => x.ReleaseVisitId,
            principalTable: "ExecutionVisits",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict
          );
        }
      );

      migrationBuilder.CreateTable(
        name: "MovementAllocationEvents",
        columns: table => new
        {
          Id = table.Column<Guid>(type: "uuid", nullable: false),
          MovementId = table.Column<Guid>(type: "uuid", nullable: false),
          Revision = table.Column<long>(type: "bigint", nullable: false),
          PreviousAllocationDispatchId = table.Column<Guid>(
            type: "uuid",
            nullable: true
          ),
          AllocatedDispatchId = table.Column<Guid>(
            type: "uuid",
            nullable: true
          ),
          Target = table.Column<string>(
            type: "character varying(16)",
            maxLength: 16,
            nullable: false
          ),
          Reason = table.Column<string>(
            type: "character varying(500)",
            maxLength: 500,
            nullable: false
          ),
          PolicyRevision = table.Column<long>(type: "bigint", nullable: false),
          ManualOverride = table.Column<bool>(type: "boolean", nullable: false),
          RecordedAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: false
          ),
          RecordedBy = table.Column<Guid>(type: "uuid", nullable: false),
        },
        constraints: table =>
        {
          table.PrimaryKey("PK_MovementAllocationEvents", x => x.Id);
          table.ForeignKey(
            name: "FK_MovementAllocationEvents_Movements_MovementId",
            column: x => x.MovementId,
            principalTable: "Movements",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict
          );
        }
      );

      migrationBuilder.CreateTable(
        name: "MovementDistanceEvidence",
        columns: table => new
        {
          Id = table.Column<Guid>(type: "uuid", nullable: false),
          MovementId = table.Column<Guid>(type: "uuid", nullable: false),
          Revision = table.Column<long>(type: "bigint", nullable: false),
          Basis = table.Column<string>(
            type: "character varying(16)",
            maxLength: 16,
            nullable: false
          ),
          Miles = table.Column<decimal>(
            type: "numeric(18,3)",
            precision: 18,
            scale: 3,
            nullable: false
          ),
          Source = table.Column<string>(
            type: "character varying(64)",
            maxLength: 64,
            nullable: false
          ),
          SourceReference = table.Column<string>(
            type: "character varying(300)",
            maxLength: 300,
            nullable: false
          ),
          Reason = table.Column<string>(
            type: "character varying(500)",
            maxLength: 500,
            nullable: false
          ),
          ObservedAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: false
          ),
          StartedAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: true
          ),
          EndedAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: true
          ),
          RecordedAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: false
          ),
          RecordedBy = table.Column<Guid>(type: "uuid", nullable: false),
        },
        constraints: table =>
        {
          table.PrimaryKey("PK_MovementDistanceEvidence", x => x.Id);
          table.ForeignKey(
            name: "FK_MovementDistanceEvidence_Movements_MovementId",
            column: x => x.MovementId,
            principalTable: "Movements",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict
          );
        }
      );

      migrationBuilder.CreateTable(
        name: "ExecutionActionReceipts",
        columns: table => new
        {
          Id = table.Column<Guid>(type: "uuid", nullable: false),
          IdempotencyKey = table.Column<Guid>(type: "uuid", nullable: false),
          SwitchId = table.Column<Guid>(type: "uuid", nullable: false),
          ParticipantId = table.Column<Guid>(type: "uuid", nullable: false),
          Action = table.Column<string>(
            type: "character varying(30)",
            maxLength: 30,
            nullable: false
          ),
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
          table.PrimaryKey("PK_ExecutionActionReceipts", x => x.Id);
          table.ForeignKey(
            name: "FK_ExecutionActionReceipts_DispatchSwitchOperations_SwitchId",
            column: x => x.SwitchId,
            principalTable: "DispatchSwitchOperations",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict
          );
          table.ForeignKey(
            name: "FK_ExecutionActionReceipts_SwitchParticipants_ParticipantId",
            column: x => x.ParticipantId,
            principalTable: "SwitchParticipants",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict
          );
        }
      );

      migrationBuilder.CreateTable(
        name: "TrailerCustodyIntervals",
        columns: table => new
        {
          Id = table.Column<Guid>(type: "uuid", nullable: false),
          TrailerId = table.Column<Guid>(type: "uuid", nullable: false),
          ParticipantId = table.Column<Guid>(type: "uuid", nullable: false),
          ReleaseVisitId = table.Column<Guid>(type: "uuid", nullable: false),
          ReceiveVisitId = table.Column<Guid>(type: "uuid", nullable: false),
          ReleasedAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: false
          ),
          ReceivedAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: true
          ),
          ReleasedBy = table.Column<Guid>(type: "uuid", nullable: false),
          ReceivedBy = table.Column<Guid>(type: "uuid", nullable: true),
          Revision = table.Column<long>(type: "bigint", nullable: false),
        },
        constraints: table =>
        {
          table.PrimaryKey("PK_TrailerCustodyIntervals", x => x.Id);
          table.ForeignKey(
            name: "FK_TrailerCustodyIntervals_ExecutionVisits_ReceiveVisitId",
            column: x => x.ReceiveVisitId,
            principalTable: "ExecutionVisits",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict
          );
          table.ForeignKey(
            name: "FK_TrailerCustodyIntervals_ExecutionVisits_ReleaseVisitId",
            column: x => x.ReleaseVisitId,
            principalTable: "ExecutionVisits",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict
          );
          table.ForeignKey(
            name: "FK_TrailerCustodyIntervals_SwitchParticipants_ParticipantId",
            column: x => x.ParticipantId,
            principalTable: "SwitchParticipants",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict
          );
          table.ForeignKey(
            name: "FK_TrailerCustodyIntervals_Trailers_TrailerId",
            column: x => x.TrailerId,
            principalTable: "Trailers",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict
          );
        }
      );

      migrationBuilder.CreateIndex(
        name: "IX_DispatchRoutePreviews_ExecutionLegId",
        table: "DispatchRoutePreviews",
        column: "ExecutionLegId"
      );

      migrationBuilder.CreateIndex(
        name: "IX_DispatchRoutePlans_DispatchId",
        table: "DispatchRoutePlans",
        column: "DispatchId",
        unique: true,
        filter: "\"ExecutionLegId\" IS NULL"
      );

      migrationBuilder.CreateIndex(
        name: "IX_DispatchRoutePlans_ExecutionLegId",
        table: "DispatchRoutePlans",
        column: "ExecutionLegId",
        unique: true,
        filter: "\"ExecutionLegId\" IS NOT NULL"
      );

      migrationBuilder.CreateIndex(
        name: "IX_DispatchRouteChoices_DispatchId",
        table: "DispatchRouteChoices",
        column: "DispatchId",
        unique: true,
        filter: "\"ExecutionLegId\" IS NULL"
      );

      migrationBuilder.CreateIndex(
        name: "IX_DispatchRouteChoices_ExecutionLegId",
        table: "DispatchRouteChoices",
        column: "ExecutionLegId",
        unique: true,
        filter: "\"ExecutionLegId\" IS NOT NULL"
      );

      migrationBuilder.CreateIndex(
        name: "IX_DispatchEtaForecasts_DispatchId",
        table: "DispatchEtaForecasts",
        column: "DispatchId",
        unique: true,
        filter: "\"ExecutionLegId\" IS NULL"
      );

      migrationBuilder.CreateIndex(
        name: "IX_DispatchEtaForecasts_ExecutionLegId",
        table: "DispatchEtaForecasts",
        column: "ExecutionLegId",
        unique: true,
        filter: "\"ExecutionLegId\" IS NOT NULL"
      );

      migrationBuilder.CreateIndex(
        name: "IX_DispatchEtaForecasts_RootExecutionLegId",
        table: "DispatchEtaForecasts",
        column: "RootExecutionLegId"
      );

      migrationBuilder.CreateIndex(
        name: "IX_DispatchDeadheads_DispatchId",
        table: "DispatchDeadheads",
        column: "DispatchId",
        unique: true,
        filter: "\"ExecutionLegId\" IS NULL"
      );

      migrationBuilder.CreateIndex(
        name: "IX_DispatchDeadheads_ExecutionLegId",
        table: "DispatchDeadheads",
        column: "ExecutionLegId",
        unique: true,
        filter: "\"ExecutionLegId\" IS NOT NULL"
      );

      migrationBuilder.CreateIndex(
        name: "IX_DispatchDeadheads_PreviousExecutionLegId",
        table: "DispatchDeadheads",
        column: "PreviousExecutionLegId"
      );

      migrationBuilder.CreateIndex(
        name: "IX_DispatchBaseRoutes_DispatchId",
        table: "DispatchBaseRoutes",
        column: "DispatchId",
        unique: true,
        filter: "\"ExecutionLegId\" IS NULL"
      );

      migrationBuilder.CreateIndex(
        name: "IX_DispatchBaseRoutes_ExecutionLegId",
        table: "DispatchBaseRoutes",
        column: "ExecutionLegId",
        unique: true,
        filter: "\"ExecutionLegId\" IS NOT NULL"
      );

      migrationBuilder.CreateIndex(
        name: "IX_DispatchSwitchOperations_IdempotencyKey",
        table: "DispatchSwitchOperations",
        column: "IdempotencyKey",
        unique: true
      );

      migrationBuilder.CreateIndex(
        name: "IX_ExecutionActionReceipts_IdempotencyKey",
        table: "ExecutionActionReceipts",
        column: "IdempotencyKey",
        unique: true
      );

      migrationBuilder.CreateIndex(
        name: "IX_ExecutionActionReceipts_ParticipantId",
        table: "ExecutionActionReceipts",
        column: "ParticipantId"
      );

      migrationBuilder.CreateIndex(
        name: "IX_ExecutionActionReceipts_SwitchId",
        table: "ExecutionActionReceipts",
        column: "SwitchId"
      );

      migrationBuilder.CreateIndex(
        name: "IX_ExecutionLegs_CoDriverId",
        table: "ExecutionLegs",
        column: "CoDriverId"
      );

      migrationBuilder.CreateIndex(
        name: "IX_ExecutionLegs_DriverId",
        table: "ExecutionLegs",
        column: "DriverId"
      );

      migrationBuilder.CreateIndex(
        name: "IX_ExecutionLegs_EndSwitchId",
        table: "ExecutionLegs",
        column: "EndSwitchId"
      );

      migrationBuilder.CreateIndex(
        name: "IX_ExecutionLegs_StartSwitchId",
        table: "ExecutionLegs",
        column: "StartSwitchId"
      );

      migrationBuilder.CreateIndex(
        name: "IX_ExecutionLegs_TrailerId",
        table: "ExecutionLegs",
        column: "TrailerId",
        unique: true,
        filter: "\"Status\" = 'active' AND \"TrailerId\" IS NOT NULL"
      );

      migrationBuilder.CreateIndex(
        name: "IX_ExecutionLegs_TripId",
        table: "ExecutionLegs",
        column: "TripId"
      );

      migrationBuilder.CreateIndex(
        name: "IX_ExecutionLegs_TruckId",
        table: "ExecutionLegs",
        column: "TruckId",
        unique: true,
        filter: "\"Status\" = 'active'"
      );

      migrationBuilder.CreateIndex(
        name: "IX_ExecutionLegs_TruckId_Status",
        table: "ExecutionLegs",
        columns: new[] { "TruckId", "Status" }
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

      migrationBuilder.CreateIndex(
        name: "IX_LoadExecutionLegs_DispatchId_ExecutionLegId",
        table: "LoadExecutionLegs",
        columns: new[] { "DispatchId", "ExecutionLegId" },
        unique: true
      );

      migrationBuilder.CreateIndex(
        name: "IX_LoadExecutionLegs_DispatchId_Sequence",
        table: "LoadExecutionLegs",
        columns: new[] { "DispatchId", "Sequence" },
        unique: true
      );

      migrationBuilder.CreateIndex(
        name: "IX_LoadExecutionLegs_ExecutionLegId",
        table: "LoadExecutionLegs",
        column: "ExecutionLegId"
      );

      migrationBuilder.CreateIndex(
        name: "IX_MovementAllocationEvents_MovementId_Revision",
        table: "MovementAllocationEvents",
        columns: new[] { "MovementId", "Revision" },
        unique: true
      );

      migrationBuilder.CreateIndex(
        name: "IX_MovementDistanceEvidence_MovementId_Revision",
        table: "MovementDistanceEvidence",
        columns: new[] { "MovementId", "Revision" },
        unique: true
      );

      migrationBuilder.CreateIndex(
        name: "IX_Movements_AllocatedDispatchId_RecordedAt_Id",
        table: "Movements",
        columns: new[] { "AllocatedDispatchId", "RecordedAt", "Id" }
      );

      migrationBuilder.CreateIndex(
        name: "IX_Movements_CarriedDispatchId",
        table: "Movements",
        column: "CarriedDispatchId"
      );

      migrationBuilder.CreateIndex(
        name: "IX_Movements_CoDriverId",
        table: "Movements",
        column: "CoDriverId"
      );

      migrationBuilder.CreateIndex(
        name: "IX_Movements_DriverId",
        table: "Movements",
        column: "DriverId"
      );

      migrationBuilder.CreateIndex(
        name: "IX_Movements_ExecutionLegId",
        table: "Movements",
        column: "ExecutionLegId"
      );

      migrationBuilder.CreateIndex(
        name: "IX_Movements_IdempotencyKey",
        table: "Movements",
        column: "IdempotencyKey",
        unique: true
      );

      migrationBuilder.CreateIndex(
        name: "IX_Movements_NextDispatchId",
        table: "Movements",
        column: "NextDispatchId"
      );

      migrationBuilder.CreateIndex(
        name: "IX_Movements_PreviousDispatchId",
        table: "Movements",
        column: "PreviousDispatchId"
      );

      migrationBuilder.CreateIndex(
        name: "IX_Movements_TrailerId",
        table: "Movements",
        column: "TrailerId"
      );

      migrationBuilder.CreateIndex(
        name: "IX_Movements_TruckId_StartedAt",
        table: "Movements",
        columns: new[] { "TruckId", "StartedAt" }
      );

      migrationBuilder.CreateIndex(
        name: "IX_SwitchParticipants_DispatchId",
        table: "SwitchParticipants",
        column: "DispatchId"
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
        name: "IX_SwitchParticipants_SwitchId_DispatchId",
        table: "SwitchParticipants",
        columns: new[] { "SwitchId", "DispatchId" },
        unique: true
      );

      migrationBuilder.CreateIndex(
        name: "IX_TrailerCustodyIntervals_ParticipantId",
        table: "TrailerCustodyIntervals",
        column: "ParticipantId",
        unique: true
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
        name: "IX_TrailerCustodyIntervals_TrailerId",
        table: "TrailerCustodyIntervals",
        column: "TrailerId",
        unique: true,
        filter: "\"ReceivedAt\" IS NULL"
      );

      migrationBuilder.AddForeignKey(
        name: "FK_DispatchBaseRoutes_ExecutionLegs_ExecutionLegId",
        table: "DispatchBaseRoutes",
        column: "ExecutionLegId",
        principalTable: "ExecutionLegs",
        principalColumn: "Id",
        onDelete: ReferentialAction.Cascade
      );

      migrationBuilder.AddForeignKey(
        name: "FK_DispatchDeadheads_ExecutionLegs_ExecutionLegId",
        table: "DispatchDeadheads",
        column: "ExecutionLegId",
        principalTable: "ExecutionLegs",
        principalColumn: "Id",
        onDelete: ReferentialAction.Cascade
      );

      migrationBuilder.AddForeignKey(
        name: "FK_DispatchDeadheads_ExecutionLegs_PreviousExecutionLegId",
        table: "DispatchDeadheads",
        column: "PreviousExecutionLegId",
        principalTable: "ExecutionLegs",
        principalColumn: "Id",
        onDelete: ReferentialAction.Restrict
      );

      migrationBuilder.AddForeignKey(
        name: "FK_DispatchEtaForecasts_ExecutionLegs_ExecutionLegId",
        table: "DispatchEtaForecasts",
        column: "ExecutionLegId",
        principalTable: "ExecutionLegs",
        principalColumn: "Id",
        onDelete: ReferentialAction.Cascade
      );

      migrationBuilder.AddForeignKey(
        name: "FK_DispatchEtaForecasts_ExecutionLegs_RootExecutionLegId",
        table: "DispatchEtaForecasts",
        column: "RootExecutionLegId",
        principalTable: "ExecutionLegs",
        principalColumn: "Id",
        onDelete: ReferentialAction.Restrict
      );

      migrationBuilder.AddForeignKey(
        name: "FK_DispatchRouteChoices_ExecutionLegs_ExecutionLegId",
        table: "DispatchRouteChoices",
        column: "ExecutionLegId",
        principalTable: "ExecutionLegs",
        principalColumn: "Id",
        onDelete: ReferentialAction.Cascade
      );

      migrationBuilder.AddForeignKey(
        name: "FK_DispatchRoutePlans_ExecutionLegs_ExecutionLegId",
        table: "DispatchRoutePlans",
        column: "ExecutionLegId",
        principalTable: "ExecutionLegs",
        principalColumn: "Id",
        onDelete: ReferentialAction.Cascade
      );

      migrationBuilder.AddForeignKey(
        name: "FK_DispatchRoutePreviews_ExecutionLegs_ExecutionLegId",
        table: "DispatchRoutePreviews",
        column: "ExecutionLegId",
        principalTable: "ExecutionLegs",
        principalColumn: "Id",
        onDelete: ReferentialAction.Cascade
      );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.Sql(
        """
        DO $$
        BEGIN
          IF EXISTS (SELECT 1 FROM "Trips")
            OR EXISTS (SELECT 1 FROM "DispatchSwitchOperations")
            OR EXISTS (SELECT 1 FROM "Movements")
            OR EXISTS (SELECT 1 FROM "MileageAllocationPolicies")
            OR EXISTS (SELECT 1 FROM "Trucks"
              WHERE "IsLocallyConfigured" OR "ConfiguredAt" IS NOT NULL)
            OR EXISTS (SELECT 1 FROM "Trailers"
              WHERE "IsLocallyConfigured" OR "ConfiguredAt" IS NOT NULL)
            OR EXISTS (SELECT 1 FROM "Drivers"
              WHERE "IsLocallyConfigured" OR "ConfiguredAt" IS NOT NULL)
          THEN
            RAISE EXCEPTION
              'Rollback would discard operational history. Roll forward.';
          END IF;
        END $$;
        """
      );

      migrationBuilder.DropForeignKey(
        name: "FK_DispatchBaseRoutes_ExecutionLegs_ExecutionLegId",
        table: "DispatchBaseRoutes"
      );

      migrationBuilder.DropForeignKey(
        name: "FK_DispatchDeadheads_ExecutionLegs_ExecutionLegId",
        table: "DispatchDeadheads"
      );

      migrationBuilder.DropForeignKey(
        name: "FK_DispatchDeadheads_ExecutionLegs_PreviousExecutionLegId",
        table: "DispatchDeadheads"
      );

      migrationBuilder.DropForeignKey(
        name: "FK_DispatchEtaForecasts_ExecutionLegs_ExecutionLegId",
        table: "DispatchEtaForecasts"
      );

      migrationBuilder.DropForeignKey(
        name: "FK_DispatchEtaForecasts_ExecutionLegs_RootExecutionLegId",
        table: "DispatchEtaForecasts"
      );

      migrationBuilder.DropForeignKey(
        name: "FK_DispatchRouteChoices_ExecutionLegs_ExecutionLegId",
        table: "DispatchRouteChoices"
      );

      migrationBuilder.DropForeignKey(
        name: "FK_DispatchRoutePlans_ExecutionLegs_ExecutionLegId",
        table: "DispatchRoutePlans"
      );

      migrationBuilder.DropForeignKey(
        name: "FK_DispatchRoutePreviews_ExecutionLegs_ExecutionLegId",
        table: "DispatchRoutePreviews"
      );

      migrationBuilder.DropTable(name: "ExecutionActionReceipts");

      migrationBuilder.DropTable(name: "LoadExecutionLegs");

      migrationBuilder.DropTable(name: "MileageAllocationPolicies");

      migrationBuilder.DropTable(name: "MovementAllocationEvents");

      migrationBuilder.DropTable(name: "MovementDistanceEvidence");

      migrationBuilder.DropTable(name: "TrailerCustodyIntervals");

      migrationBuilder.DropTable(name: "Movements");

      migrationBuilder.DropTable(name: "SwitchParticipants");

      migrationBuilder.DropTable(name: "ExecutionLegs");

      migrationBuilder.DropTable(name: "ExecutionVisits");

      migrationBuilder.DropTable(name: "DispatchSwitchOperations");

      migrationBuilder.DropTable(name: "Trips");

      migrationBuilder.DropIndex(
        name: "IX_DispatchRoutePreviews_ExecutionLegId",
        table: "DispatchRoutePreviews"
      );

      migrationBuilder.DropIndex(
        name: "IX_DispatchRoutePlans_DispatchId",
        table: "DispatchRoutePlans"
      );

      migrationBuilder.DropIndex(
        name: "IX_DispatchRoutePlans_ExecutionLegId",
        table: "DispatchRoutePlans"
      );

      migrationBuilder.DropIndex(
        name: "IX_DispatchRouteChoices_DispatchId",
        table: "DispatchRouteChoices"
      );

      migrationBuilder.DropIndex(
        name: "IX_DispatchRouteChoices_ExecutionLegId",
        table: "DispatchRouteChoices"
      );

      migrationBuilder.DropIndex(
        name: "IX_DispatchEtaForecasts_DispatchId",
        table: "DispatchEtaForecasts"
      );

      migrationBuilder.DropIndex(
        name: "IX_DispatchEtaForecasts_ExecutionLegId",
        table: "DispatchEtaForecasts"
      );

      migrationBuilder.DropIndex(
        name: "IX_DispatchEtaForecasts_RootExecutionLegId",
        table: "DispatchEtaForecasts"
      );

      migrationBuilder.DropIndex(
        name: "IX_DispatchDeadheads_DispatchId",
        table: "DispatchDeadheads"
      );

      migrationBuilder.DropIndex(
        name: "IX_DispatchDeadheads_ExecutionLegId",
        table: "DispatchDeadheads"
      );

      migrationBuilder.DropIndex(
        name: "IX_DispatchDeadheads_PreviousExecutionLegId",
        table: "DispatchDeadheads"
      );

      migrationBuilder.DropIndex(
        name: "IX_DispatchBaseRoutes_DispatchId",
        table: "DispatchBaseRoutes"
      );

      migrationBuilder.DropIndex(
        name: "IX_DispatchBaseRoutes_ExecutionLegId",
        table: "DispatchBaseRoutes"
      );

      migrationBuilder.DropColumn(
        name: "ConfigurationRevision",
        table: "Trucks"
      );

      migrationBuilder.DropColumn(name: "ConfiguredAt", table: "Trucks");

      migrationBuilder.DropColumn(name: "ConfiguredBy", table: "Trucks");

      migrationBuilder.DropColumn(name: "ImportedIsActive", table: "Trucks");

      migrationBuilder.DropColumn(name: "ImportedVin", table: "Trucks");

      migrationBuilder.DropColumn(name: "IsLocallyConfigured", table: "Trucks");

      migrationBuilder.DropColumn(
        name: "ConfigurationRevision",
        table: "Trailers"
      );

      migrationBuilder.DropColumn(name: "ConfiguredAt", table: "Trailers");

      migrationBuilder.DropColumn(name: "ConfiguredBy", table: "Trailers");

      migrationBuilder.DropColumn(name: "ImportedIsActive", table: "Trailers");

      migrationBuilder.DropColumn(name: "ImportedVin", table: "Trailers");

      migrationBuilder.DropColumn(
        name: "IsLocallyConfigured",
        table: "Trailers"
      );

      migrationBuilder.DropColumn(
        name: "ConfigurationRevision",
        table: "Drivers"
      );

      migrationBuilder.DropColumn(name: "ConfiguredAt", table: "Drivers");

      migrationBuilder.DropColumn(name: "ConfiguredBy", table: "Drivers");

      migrationBuilder.DropColumn(name: "ImportedFuelCard", table: "Drivers");

      migrationBuilder.DropColumn(name: "ImportedIsActive", table: "Drivers");

      migrationBuilder.DropColumn(name: "ImportedName", table: "Drivers");

      migrationBuilder.DropColumn(
        name: "IsLocallyConfigured",
        table: "Drivers"
      );

      migrationBuilder.DropColumn(
        name: "ExecutionLegId",
        table: "DispatchRoutePreviews"
      );

      migrationBuilder.DropColumn(
        name: "AssignmentRevision",
        table: "DispatchRoutePlans"
      );

      migrationBuilder.DropColumn(
        name: "ExecutionLegId",
        table: "DispatchRoutePlans"
      );

      migrationBuilder.DropColumn(
        name: "ExecutionLegId",
        table: "DispatchRouteChoices"
      );

      migrationBuilder.DropColumn(
        name: "AssignmentRevision",
        table: "DispatchEtaForecasts"
      );

      migrationBuilder.DropColumn(
        name: "ExecutionLegId",
        table: "DispatchEtaForecasts"
      );

      migrationBuilder.DropColumn(
        name: "RootExecutionLegId",
        table: "DispatchEtaForecasts"
      );

      migrationBuilder.DropColumn(
        name: "ExecutionLegId",
        table: "DispatchDeadheads"
      );

      migrationBuilder.DropColumn(
        name: "PreviousExecutionLegId",
        table: "DispatchDeadheads"
      );

      migrationBuilder.DropColumn(
        name: "ExecutionLegId",
        table: "DispatchBaseRoutes"
      );

      migrationBuilder.CreateIndex(
        name: "IX_DispatchRoutePlans_DispatchId",
        table: "DispatchRoutePlans",
        column: "DispatchId",
        unique: true
      );

      migrationBuilder.CreateIndex(
        name: "IX_DispatchRouteChoices_DispatchId",
        table: "DispatchRouteChoices",
        column: "DispatchId",
        unique: true
      );

      migrationBuilder.CreateIndex(
        name: "IX_DispatchEtaForecasts_DispatchId",
        table: "DispatchEtaForecasts",
        column: "DispatchId",
        unique: true
      );

      migrationBuilder.CreateIndex(
        name: "IX_DispatchDeadheads_DispatchId",
        table: "DispatchDeadheads",
        column: "DispatchId",
        unique: true
      );

      migrationBuilder.CreateIndex(
        name: "IX_DispatchBaseRoutes_DispatchId",
        table: "DispatchBaseRoutes",
        column: "DispatchId",
        unique: true
      );
    }
  }
}
