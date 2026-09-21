using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IntroduceCompanies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Trucks_ExternalId",
                table: "Trucks");

            migrationBuilder.DropIndex(
                name: "IX_Trucks_UnitNumber",
                table: "Trucks");

            migrationBuilder.DropIndex(
                name: "IX_Trailers_ExternalId",
                table: "Trailers");

            migrationBuilder.DropIndex(
                name: "IX_Trailers_UnitNumber",
                table: "Trailers");

            migrationBuilder.DropIndex(
                name: "IX_Movements_IdempotencyKey",
                table: "Movements");

            migrationBuilder.DropIndex(
                name: "IX_FuelImportSources_GmailMessageId",
                table: "FuelImportSources");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_IdempotencyKey",
                table: "Expenses");

            migrationBuilder.DropIndex(
                name: "IX_ExecutionSourceReceipts_IdempotencyKey",
                table: "ExecutionSourceReceipts");

            migrationBuilder.DropIndex(
                name: "IX_ExecutionActionReceipts_IdempotencyKey",
                table: "ExecutionActionReceipts");

            migrationBuilder.DropIndex(
                name: "IX_Drivers_ExternalId",
                table: "Drivers");

            migrationBuilder.DropIndex(
                name: "IX_DispatchWorkspaceRevisions_IdempotencyKey",
                table: "DispatchWorkspaceRevisions");

            migrationBuilder.DropIndex(
                name: "IX_DispatchSwitchOperations_IdempotencyKey",
                table: "DispatchSwitchOperations");

            migrationBuilder.DropPrimaryKey(
                name: "PK_DispatchSourceLinks",
                table: "DispatchSourceLinks");

            migrationBuilder.DropIndex(
                name: "IX_Dispatches_LoadNumber",
                table: "Dispatches");

            migrationBuilder.DropIndex(
                name: "IX_Customers_NormalizedName",
                table: "Customers");

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "Users",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "Trucks",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "TruckPlanningProfiles",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "TruckLocationReadings",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "TruckFuelPlans",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "Trips",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "Trailers",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "TrailerCustodyIntervals",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "SynchronizationCheckpoints",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "SwitchParticipants",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "SourceRoadRequests",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "ShipmentSaveReceipts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "Shipments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "RouteRecalculationAttempts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "PlanningRefreshRequests",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "OdometerPositions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "OdometerIntervals",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "OdometerCaptureCheckpoints",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "Movements",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "MovementDistanceEvidence",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "MovementAllocationEvents",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "MileageCaptureGaps",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "MileageAllocationPolicies",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "LoadExecutionLegs",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "FuelTransactions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "FuelImportSources",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "FuelDiscounts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "FleetPlanningSettings",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "Expenses",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "ExpenseAttributions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "ExpenseAttributionEvents",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "ExecutionSourceReceipts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "ExecutionPlanningChanges",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "ExecutionLegStops",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "ExecutionLegs",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "ExecutionLegRevisions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "ExecutionActionReceipts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "Drivers",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "DriverHosReadings",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "DispatchWorkspaces",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "DispatchWorkspaceRevisions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "DispatchSwitchOperations",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "DispatchStops",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "DispatchStopCompletionEvents",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "DispatchSourceLinks",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "DispatchSettings",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "DispatchRoutePreviews",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "DispatchRoutePlans",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "DispatchRouteChoices",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "DispatchRates",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "DispatchNumberCounters",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "DispatchEtaForecasts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "Dispatches",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "DispatchDocuments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "DispatchDeadheads",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "DispatchBaseRoutes",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "DispatchActivityThreads",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "DispatchActivityEntries",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "Customers",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "BorderSaveReceipts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "BorderCrossings",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddPrimaryKey(
                name: "PK_DispatchSourceLinks",
                table: "DispatchSourceLinks",
                columns: new[] { "CompanyId", "Provider", "ExternalId" });

            migrationBuilder.CreateTable(
                name: "Companies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Companies", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_CompanyId",
                table: "Users",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Trucks_CompanyId",
                table: "Trucks",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Trucks_CompanyId_ExternalId",
                table: "Trucks",
                columns: new[] { "CompanyId", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Trucks_CompanyId_UnitNumber",
                table: "Trucks",
                columns: new[] { "CompanyId", "UnitNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TruckPlanningProfiles_CompanyId",
                table: "TruckPlanningProfiles",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_TruckLocationReadings_CompanyId",
                table: "TruckLocationReadings",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_TruckFuelPlans_CompanyId",
                table: "TruckFuelPlans",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Trips_CompanyId",
                table: "Trips",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Trailers_CompanyId",
                table: "Trailers",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Trailers_CompanyId_ExternalId",
                table: "Trailers",
                columns: new[] { "CompanyId", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Trailers_CompanyId_UnitNumber",
                table: "Trailers",
                columns: new[] { "CompanyId", "UnitNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrailerCustodyIntervals_CompanyId",
                table: "TrailerCustodyIntervals",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_SynchronizationCheckpoints_CompanyId",
                table: "SynchronizationCheckpoints",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_SwitchParticipants_CompanyId",
                table: "SwitchParticipants",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentSaveReceipts_CompanyId",
                table: "ShipmentSaveReceipts",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_CompanyId",
                table: "Shipments",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_RouteRecalculationAttempts_CompanyId",
                table: "RouteRecalculationAttempts",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_OdometerPositions_CompanyId",
                table: "OdometerPositions",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_OdometerIntervals_CompanyId",
                table: "OdometerIntervals",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_OdometerCaptureCheckpoints_CompanyId",
                table: "OdometerCaptureCheckpoints",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Movements_CompanyId",
                table: "Movements",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Movements_CompanyId_IdempotencyKey",
                table: "Movements",
                columns: new[] { "CompanyId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MovementDistanceEvidence_CompanyId",
                table: "MovementDistanceEvidence",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_MovementAllocationEvents_CompanyId",
                table: "MovementAllocationEvents",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_MileageCaptureGaps_CompanyId",
                table: "MileageCaptureGaps",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_MileageAllocationPolicies_CompanyId",
                table: "MileageAllocationPolicies",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_LoadExecutionLegs_CompanyId",
                table: "LoadExecutionLegs",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_FuelTransactions_CompanyId",
                table: "FuelTransactions",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_FuelImportSources_CompanyId",
                table: "FuelImportSources",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_FuelImportSources_CompanyId_GmailMessageId",
                table: "FuelImportSources",
                columns: new[] { "CompanyId", "GmailMessageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FuelDiscounts_CompanyId",
                table: "FuelDiscounts",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_FleetPlanningSettings_CompanyId",
                table: "FleetPlanningSettings",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_CompanyId",
                table: "Expenses",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_CompanyId_IdempotencyKey",
                table: "Expenses",
                columns: new[] { "CompanyId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseAttributions_CompanyId",
                table: "ExpenseAttributions",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseAttributionEvents_CompanyId",
                table: "ExpenseAttributionEvents",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionSourceReceipts_CompanyId",
                table: "ExecutionSourceReceipts",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionSourceReceipts_CompanyId_IdempotencyKey",
                table: "ExecutionSourceReceipts",
                columns: new[] { "CompanyId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionLegStops_CompanyId",
                table: "ExecutionLegStops",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionLegs_CompanyId",
                table: "ExecutionLegs",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionLegRevisions_CompanyId",
                table: "ExecutionLegRevisions",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionActionReceipts_CompanyId",
                table: "ExecutionActionReceipts",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionActionReceipts_CompanyId_IdempotencyKey",
                table: "ExecutionActionReceipts",
                columns: new[] { "CompanyId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Drivers_CompanyId",
                table: "Drivers",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Drivers_CompanyId_ExternalId",
                table: "Drivers",
                columns: new[] { "CompanyId", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DriverHosReadings_CompanyId",
                table: "DriverHosReadings",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_DispatchWorkspaces_CompanyId",
                table: "DispatchWorkspaces",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_DispatchWorkspaceRevisions_CompanyId",
                table: "DispatchWorkspaceRevisions",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_DispatchWorkspaceRevisions_CompanyId_IdempotencyKey",
                table: "DispatchWorkspaceRevisions",
                columns: new[] { "CompanyId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DispatchSwitchOperations_CompanyId",
                table: "DispatchSwitchOperations",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_DispatchSwitchOperations_CompanyId_IdempotencyKey",
                table: "DispatchSwitchOperations",
                columns: new[] { "CompanyId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DispatchStops_CompanyId",
                table: "DispatchStops",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_DispatchStopCompletionEvents_CompanyId",
                table: "DispatchStopCompletionEvents",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_DispatchSourceLinks_CompanyId",
                table: "DispatchSourceLinks",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_DispatchSettings_CompanyId",
                table: "DispatchSettings",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_DispatchRoutePreviews_CompanyId",
                table: "DispatchRoutePreviews",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_DispatchRoutePlans_CompanyId",
                table: "DispatchRoutePlans",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_DispatchRouteChoices_CompanyId",
                table: "DispatchRouteChoices",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_DispatchRates_CompanyId",
                table: "DispatchRates",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_DispatchNumberCounters_CompanyId",
                table: "DispatchNumberCounters",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_DispatchEtaForecasts_CompanyId",
                table: "DispatchEtaForecasts",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Dispatches_CompanyId",
                table: "Dispatches",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Dispatches_CompanyId_LoadNumber",
                table: "Dispatches",
                columns: new[] { "CompanyId", "LoadNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DispatchDocuments_CompanyId",
                table: "DispatchDocuments",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_DispatchDeadheads_CompanyId",
                table: "DispatchDeadheads",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_DispatchBaseRoutes_CompanyId",
                table: "DispatchBaseRoutes",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_DispatchActivityThreads_CompanyId",
                table: "DispatchActivityThreads",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_DispatchActivityEntries_CompanyId",
                table: "DispatchActivityEntries",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_CompanyId",
                table: "Customers",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_CompanyId_NormalizedName",
                table: "Customers",
                columns: new[] { "CompanyId", "NormalizedName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BorderSaveReceipts_CompanyId",
                table: "BorderSaveReceipts",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_BorderCrossings_CompanyId",
                table: "BorderCrossings",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Companies_Key",
                table: "Companies",
                column: "Key",
                unique: true);

            // Everything already here is AMF's: this system was built for
            // one carrier and has only ever held that carrier's work. The
            // column arrives with the empty guid as its default, so every
            // existing row is pointed at AMF before anything reads it -
            // otherwise the filters added in this same release would hide
            // the whole database from the people using it.
            migrationBuilder.Sql(
                """
                INSERT INTO "Companies" ("Id", "Key", "Name", "IsActive", "CreatedAt")
                VALUES ('a0f0a0f0-0000-4000-8000-000000000001', 'amfcarrier', 'AMF Carrier', true, now() at time zone 'utc')
                ON CONFLICT ("Id") DO NOTHING;
                """);
            migrationBuilder.Sql(
                """UPDATE "BorderCrossings" SET "CompanyId" = 'a0f0a0f0-0000-4000-8000-000000000001' WHERE "CompanyId" = '00000000-0000-0000-0000-000000000000';""");
            migrationBuilder.Sql(
                """UPDATE "BorderSaveReceipts" SET "CompanyId" = 'a0f0a0f0-0000-4000-8000-000000000001' WHERE "CompanyId" = '00000000-0000-0000-0000-000000000000';""");
            migrationBuilder.Sql(
                """UPDATE "Customers" SET "CompanyId" = 'a0f0a0f0-0000-4000-8000-000000000001' WHERE "CompanyId" = '00000000-0000-0000-0000-000000000000';""");
            migrationBuilder.Sql(
                """UPDATE "DispatchActivityEntries" SET "CompanyId" = 'a0f0a0f0-0000-4000-8000-000000000001' WHERE "CompanyId" = '00000000-0000-0000-0000-000000000000';""");
            migrationBuilder.Sql(
                """UPDATE "DispatchActivityThreads" SET "CompanyId" = 'a0f0a0f0-0000-4000-8000-000000000001' WHERE "CompanyId" = '00000000-0000-0000-0000-000000000000';""");
            migrationBuilder.Sql(
                """UPDATE "DispatchBaseRoutes" SET "CompanyId" = 'a0f0a0f0-0000-4000-8000-000000000001' WHERE "CompanyId" = '00000000-0000-0000-0000-000000000000';""");
            migrationBuilder.Sql(
                """UPDATE "DispatchDeadheads" SET "CompanyId" = 'a0f0a0f0-0000-4000-8000-000000000001' WHERE "CompanyId" = '00000000-0000-0000-0000-000000000000';""");
            migrationBuilder.Sql(
                """UPDATE "DispatchDocuments" SET "CompanyId" = 'a0f0a0f0-0000-4000-8000-000000000001' WHERE "CompanyId" = '00000000-0000-0000-0000-000000000000';""");
            migrationBuilder.Sql(
                """UPDATE "DispatchEtaForecasts" SET "CompanyId" = 'a0f0a0f0-0000-4000-8000-000000000001' WHERE "CompanyId" = '00000000-0000-0000-0000-000000000000';""");
            migrationBuilder.Sql(
                """UPDATE "DispatchNumberCounters" SET "CompanyId" = 'a0f0a0f0-0000-4000-8000-000000000001' WHERE "CompanyId" = '00000000-0000-0000-0000-000000000000';""");
            migrationBuilder.Sql(
                """UPDATE "DispatchRates" SET "CompanyId" = 'a0f0a0f0-0000-4000-8000-000000000001' WHERE "CompanyId" = '00000000-0000-0000-0000-000000000000';""");
            migrationBuilder.Sql(
                """UPDATE "DispatchRouteChoices" SET "CompanyId" = 'a0f0a0f0-0000-4000-8000-000000000001' WHERE "CompanyId" = '00000000-0000-0000-0000-000000000000';""");
            migrationBuilder.Sql(
                """UPDATE "DispatchRoutePlans" SET "CompanyId" = 'a0f0a0f0-0000-4000-8000-000000000001' WHERE "CompanyId" = '00000000-0000-0000-0000-000000000000';""");
            migrationBuilder.Sql(
                """UPDATE "DispatchRoutePreviews" SET "CompanyId" = 'a0f0a0f0-0000-4000-8000-000000000001' WHERE "CompanyId" = '00000000-0000-0000-0000-000000000000';""");
            migrationBuilder.Sql(
                """UPDATE "DispatchSettings" SET "CompanyId" = 'a0f0a0f0-0000-4000-8000-000000000001' WHERE "CompanyId" = '00000000-0000-0000-0000-000000000000';""");
            migrationBuilder.Sql(
                """UPDATE "DispatchSourceLinks" SET "CompanyId" = 'a0f0a0f0-0000-4000-8000-000000000001' WHERE "CompanyId" = '00000000-0000-0000-0000-000000000000';""");
            migrationBuilder.Sql(
                """UPDATE "DispatchStopCompletionEvents" SET "CompanyId" = 'a0f0a0f0-0000-4000-8000-000000000001' WHERE "CompanyId" = '00000000-0000-0000-0000-000000000000';""");
            migrationBuilder.Sql(
                """UPDATE "DispatchStops" SET "CompanyId" = 'a0f0a0f0-0000-4000-8000-000000000001' WHERE "CompanyId" = '00000000-0000-0000-0000-000000000000';""");
            migrationBuilder.Sql(
                """UPDATE "DispatchSwitchOperations" SET "CompanyId" = 'a0f0a0f0-0000-4000-8000-000000000001' WHERE "CompanyId" = '00000000-0000-0000-0000-000000000000';""");
            migrationBuilder.Sql(
                """UPDATE "DispatchWorkspaceRevisions" SET "CompanyId" = 'a0f0a0f0-0000-4000-8000-000000000001' WHERE "CompanyId" = '00000000-0000-0000-0000-000000000000';""");
            migrationBuilder.Sql(
                """UPDATE "DispatchWorkspaces" SET "CompanyId" = 'a0f0a0f0-0000-4000-8000-000000000001' WHERE "CompanyId" = '00000000-0000-0000-0000-000000000000';""");
            migrationBuilder.Sql(
                """UPDATE "Dispatches" SET "CompanyId" = 'a0f0a0f0-0000-4000-8000-000000000001' WHERE "CompanyId" = '00000000-0000-0000-0000-000000000000';""");
            migrationBuilder.Sql(
                """UPDATE "DriverHosReadings" SET "CompanyId" = 'a0f0a0f0-0000-4000-8000-000000000001' WHERE "CompanyId" = '00000000-0000-0000-0000-000000000000';""");
            migrationBuilder.Sql(
                """UPDATE "Drivers" SET "CompanyId" = 'a0f0a0f0-0000-4000-8000-000000000001' WHERE "CompanyId" = '00000000-0000-0000-0000-000000000000';""");
            migrationBuilder.Sql(
                """UPDATE "ExecutionActionReceipts" SET "CompanyId" = 'a0f0a0f0-0000-4000-8000-000000000001' WHERE "CompanyId" = '00000000-0000-0000-0000-000000000000';""");
            // Accepted execution history is immutable, and a trigger holds
            // it to that. Saying whose a revision is changes nothing that
            // was accepted, so the trigger stands aside for this one
            // statement - inside the migration's transaction, under the
            // table lock the ALTER takes - and is put straight back.
            migrationBuilder.Sql(
                """
                ALTER TABLE "ExecutionLegRevisions" DISABLE TRIGGER execution_history_immutable;
                UPDATE "ExecutionLegRevisions" SET "CompanyId" = 'a0f0a0f0-0000-4000-8000-000000000001' WHERE "CompanyId" = '00000000-0000-0000-0000-000000000000';
                ALTER TABLE "ExecutionLegRevisions" ENABLE TRIGGER execution_history_immutable;
                """);
            migrationBuilder.Sql(
                """UPDATE "ExecutionLegStops" SET "CompanyId" = 'a0f0a0f0-0000-4000-8000-000000000001' WHERE "CompanyId" = '00000000-0000-0000-0000-000000000000';""");
            migrationBuilder.Sql(
                """UPDATE "ExecutionLegs" SET "CompanyId" = 'a0f0a0f0-0000-4000-8000-000000000001' WHERE "CompanyId" = '00000000-0000-0000-0000-000000000000';""");
            migrationBuilder.Sql(
                """UPDATE "ExecutionPlanningChanges" SET "CompanyId" = 'a0f0a0f0-0000-4000-8000-000000000001' WHERE "CompanyId" = '00000000-0000-0000-0000-000000000000';""");
            migrationBuilder.Sql(
                """UPDATE "ExecutionSourceReceipts" SET "CompanyId" = 'a0f0a0f0-0000-4000-8000-000000000001' WHERE "CompanyId" = '00000000-0000-0000-0000-000000000000';""");
            migrationBuilder.Sql(
                """UPDATE "ExpenseAttributionEvents" SET "CompanyId" = 'a0f0a0f0-0000-4000-8000-000000000001' WHERE "CompanyId" = '00000000-0000-0000-0000-000000000000';""");
            migrationBuilder.Sql(
                """UPDATE "ExpenseAttributions" SET "CompanyId" = 'a0f0a0f0-0000-4000-8000-000000000001' WHERE "CompanyId" = '00000000-0000-0000-0000-000000000000';""");
            migrationBuilder.Sql(
                """UPDATE "Expenses" SET "CompanyId" = 'a0f0a0f0-0000-4000-8000-000000000001' WHERE "CompanyId" = '00000000-0000-0000-0000-000000000000';""");
            migrationBuilder.Sql(
                """UPDATE "FleetPlanningSettings" SET "CompanyId" = 'a0f0a0f0-0000-4000-8000-000000000001' WHERE "CompanyId" = '00000000-0000-0000-0000-000000000000';""");
            migrationBuilder.Sql(
                """UPDATE "FuelDiscounts" SET "CompanyId" = 'a0f0a0f0-0000-4000-8000-000000000001' WHERE "CompanyId" = '00000000-0000-0000-0000-000000000000';""");
            migrationBuilder.Sql(
                """UPDATE "FuelImportSources" SET "CompanyId" = 'a0f0a0f0-0000-4000-8000-000000000001' WHERE "CompanyId" = '00000000-0000-0000-0000-000000000000';""");
            migrationBuilder.Sql(
                """UPDATE "FuelTransactions" SET "CompanyId" = 'a0f0a0f0-0000-4000-8000-000000000001' WHERE "CompanyId" = '00000000-0000-0000-0000-000000000000';""");
            migrationBuilder.Sql(
                """UPDATE "LoadExecutionLegs" SET "CompanyId" = 'a0f0a0f0-0000-4000-8000-000000000001' WHERE "CompanyId" = '00000000-0000-0000-0000-000000000000';""");
            migrationBuilder.Sql(
                """UPDATE "MileageAllocationPolicies" SET "CompanyId" = 'a0f0a0f0-0000-4000-8000-000000000001' WHERE "CompanyId" = '00000000-0000-0000-0000-000000000000';""");
            migrationBuilder.Sql(
                """UPDATE "MileageCaptureGaps" SET "CompanyId" = 'a0f0a0f0-0000-4000-8000-000000000001' WHERE "CompanyId" = '00000000-0000-0000-0000-000000000000';""");
            migrationBuilder.Sql(
                """UPDATE "MovementAllocationEvents" SET "CompanyId" = 'a0f0a0f0-0000-4000-8000-000000000001' WHERE "CompanyId" = '00000000-0000-0000-0000-000000000000';""");
            migrationBuilder.Sql(
                """UPDATE "MovementDistanceEvidence" SET "CompanyId" = 'a0f0a0f0-0000-4000-8000-000000000001' WHERE "CompanyId" = '00000000-0000-0000-0000-000000000000';""");
            migrationBuilder.Sql(
                """UPDATE "Movements" SET "CompanyId" = 'a0f0a0f0-0000-4000-8000-000000000001' WHERE "CompanyId" = '00000000-0000-0000-0000-000000000000';""");
            migrationBuilder.Sql(
                """UPDATE "OdometerCaptureCheckpoints" SET "CompanyId" = 'a0f0a0f0-0000-4000-8000-000000000001' WHERE "CompanyId" = '00000000-0000-0000-0000-000000000000';""");
            migrationBuilder.Sql(
                """UPDATE "OdometerIntervals" SET "CompanyId" = 'a0f0a0f0-0000-4000-8000-000000000001' WHERE "CompanyId" = '00000000-0000-0000-0000-000000000000';""");
            migrationBuilder.Sql(
                """UPDATE "OdometerPositions" SET "CompanyId" = 'a0f0a0f0-0000-4000-8000-000000000001' WHERE "CompanyId" = '00000000-0000-0000-0000-000000000000';""");
            migrationBuilder.Sql(
                """UPDATE "PlanningRefreshRequests" SET "CompanyId" = 'a0f0a0f0-0000-4000-8000-000000000001' WHERE "CompanyId" = '00000000-0000-0000-0000-000000000000';""");
            migrationBuilder.Sql(
                """UPDATE "RouteRecalculationAttempts" SET "CompanyId" = 'a0f0a0f0-0000-4000-8000-000000000001' WHERE "CompanyId" = '00000000-0000-0000-0000-000000000000';""");
            migrationBuilder.Sql(
                """UPDATE "ShipmentSaveReceipts" SET "CompanyId" = 'a0f0a0f0-0000-4000-8000-000000000001' WHERE "CompanyId" = '00000000-0000-0000-0000-000000000000';""");
            migrationBuilder.Sql(
                """UPDATE "Shipments" SET "CompanyId" = 'a0f0a0f0-0000-4000-8000-000000000001' WHERE "CompanyId" = '00000000-0000-0000-0000-000000000000';""");
            migrationBuilder.Sql(
                """UPDATE "SourceRoadRequests" SET "CompanyId" = 'a0f0a0f0-0000-4000-8000-000000000001' WHERE "CompanyId" = '00000000-0000-0000-0000-000000000000';""");
            migrationBuilder.Sql(
                """UPDATE "SwitchParticipants" SET "CompanyId" = 'a0f0a0f0-0000-4000-8000-000000000001' WHERE "CompanyId" = '00000000-0000-0000-0000-000000000000';""");
            migrationBuilder.Sql(
                """UPDATE "SynchronizationCheckpoints" SET "CompanyId" = 'a0f0a0f0-0000-4000-8000-000000000001' WHERE "CompanyId" = '00000000-0000-0000-0000-000000000000';""");
            migrationBuilder.Sql(
                """UPDATE "TrailerCustodyIntervals" SET "CompanyId" = 'a0f0a0f0-0000-4000-8000-000000000001' WHERE "CompanyId" = '00000000-0000-0000-0000-000000000000';""");
            migrationBuilder.Sql(
                """UPDATE "Trailers" SET "CompanyId" = 'a0f0a0f0-0000-4000-8000-000000000001' WHERE "CompanyId" = '00000000-0000-0000-0000-000000000000';""");
            migrationBuilder.Sql(
                """UPDATE "Trips" SET "CompanyId" = 'a0f0a0f0-0000-4000-8000-000000000001' WHERE "CompanyId" = '00000000-0000-0000-0000-000000000000';""");
            migrationBuilder.Sql(
                """UPDATE "TruckFuelPlans" SET "CompanyId" = 'a0f0a0f0-0000-4000-8000-000000000001' WHERE "CompanyId" = '00000000-0000-0000-0000-000000000000';""");
            migrationBuilder.Sql(
                """UPDATE "TruckLocationReadings" SET "CompanyId" = 'a0f0a0f0-0000-4000-8000-000000000001' WHERE "CompanyId" = '00000000-0000-0000-0000-000000000000';""");
            migrationBuilder.Sql(
                """UPDATE "TruckPlanningProfiles" SET "CompanyId" = 'a0f0a0f0-0000-4000-8000-000000000001' WHERE "CompanyId" = '00000000-0000-0000-0000-000000000000';""");
            migrationBuilder.Sql(
                """UPDATE "Trucks" SET "CompanyId" = 'a0f0a0f0-0000-4000-8000-000000000001' WHERE "CompanyId" = '00000000-0000-0000-0000-000000000000';""");
            migrationBuilder.Sql(
                """UPDATE "Users" SET "CompanyId" = 'a0f0a0f0-0000-4000-8000-000000000001' WHERE "CompanyId" = '00000000-0000-0000-0000-000000000000';""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Companies");

            migrationBuilder.DropIndex(
                name: "IX_Users_CompanyId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Trucks_CompanyId",
                table: "Trucks");

            migrationBuilder.DropIndex(
                name: "IX_Trucks_CompanyId_ExternalId",
                table: "Trucks");

            migrationBuilder.DropIndex(
                name: "IX_Trucks_CompanyId_UnitNumber",
                table: "Trucks");

            migrationBuilder.DropIndex(
                name: "IX_TruckPlanningProfiles_CompanyId",
                table: "TruckPlanningProfiles");

            migrationBuilder.DropIndex(
                name: "IX_TruckLocationReadings_CompanyId",
                table: "TruckLocationReadings");

            migrationBuilder.DropIndex(
                name: "IX_TruckFuelPlans_CompanyId",
                table: "TruckFuelPlans");

            migrationBuilder.DropIndex(
                name: "IX_Trips_CompanyId",
                table: "Trips");

            migrationBuilder.DropIndex(
                name: "IX_Trailers_CompanyId",
                table: "Trailers");

            migrationBuilder.DropIndex(
                name: "IX_Trailers_CompanyId_ExternalId",
                table: "Trailers");

            migrationBuilder.DropIndex(
                name: "IX_Trailers_CompanyId_UnitNumber",
                table: "Trailers");

            migrationBuilder.DropIndex(
                name: "IX_TrailerCustodyIntervals_CompanyId",
                table: "TrailerCustodyIntervals");

            migrationBuilder.DropIndex(
                name: "IX_SynchronizationCheckpoints_CompanyId",
                table: "SynchronizationCheckpoints");

            migrationBuilder.DropIndex(
                name: "IX_SwitchParticipants_CompanyId",
                table: "SwitchParticipants");

            migrationBuilder.DropIndex(
                name: "IX_ShipmentSaveReceipts_CompanyId",
                table: "ShipmentSaveReceipts");

            migrationBuilder.DropIndex(
                name: "IX_Shipments_CompanyId",
                table: "Shipments");

            migrationBuilder.DropIndex(
                name: "IX_RouteRecalculationAttempts_CompanyId",
                table: "RouteRecalculationAttempts");

            migrationBuilder.DropIndex(
                name: "IX_OdometerPositions_CompanyId",
                table: "OdometerPositions");

            migrationBuilder.DropIndex(
                name: "IX_OdometerIntervals_CompanyId",
                table: "OdometerIntervals");

            migrationBuilder.DropIndex(
                name: "IX_OdometerCaptureCheckpoints_CompanyId",
                table: "OdometerCaptureCheckpoints");

            migrationBuilder.DropIndex(
                name: "IX_Movements_CompanyId",
                table: "Movements");

            migrationBuilder.DropIndex(
                name: "IX_Movements_CompanyId_IdempotencyKey",
                table: "Movements");

            migrationBuilder.DropIndex(
                name: "IX_MovementDistanceEvidence_CompanyId",
                table: "MovementDistanceEvidence");

            migrationBuilder.DropIndex(
                name: "IX_MovementAllocationEvents_CompanyId",
                table: "MovementAllocationEvents");

            migrationBuilder.DropIndex(
                name: "IX_MileageCaptureGaps_CompanyId",
                table: "MileageCaptureGaps");

            migrationBuilder.DropIndex(
                name: "IX_MileageAllocationPolicies_CompanyId",
                table: "MileageAllocationPolicies");

            migrationBuilder.DropIndex(
                name: "IX_LoadExecutionLegs_CompanyId",
                table: "LoadExecutionLegs");

            migrationBuilder.DropIndex(
                name: "IX_FuelTransactions_CompanyId",
                table: "FuelTransactions");

            migrationBuilder.DropIndex(
                name: "IX_FuelImportSources_CompanyId",
                table: "FuelImportSources");

            migrationBuilder.DropIndex(
                name: "IX_FuelImportSources_CompanyId_GmailMessageId",
                table: "FuelImportSources");

            migrationBuilder.DropIndex(
                name: "IX_FuelDiscounts_CompanyId",
                table: "FuelDiscounts");

            migrationBuilder.DropIndex(
                name: "IX_FleetPlanningSettings_CompanyId",
                table: "FleetPlanningSettings");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_CompanyId",
                table: "Expenses");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_CompanyId_IdempotencyKey",
                table: "Expenses");

            migrationBuilder.DropIndex(
                name: "IX_ExpenseAttributions_CompanyId",
                table: "ExpenseAttributions");

            migrationBuilder.DropIndex(
                name: "IX_ExpenseAttributionEvents_CompanyId",
                table: "ExpenseAttributionEvents");

            migrationBuilder.DropIndex(
                name: "IX_ExecutionSourceReceipts_CompanyId",
                table: "ExecutionSourceReceipts");

            migrationBuilder.DropIndex(
                name: "IX_ExecutionSourceReceipts_CompanyId_IdempotencyKey",
                table: "ExecutionSourceReceipts");

            migrationBuilder.DropIndex(
                name: "IX_ExecutionLegStops_CompanyId",
                table: "ExecutionLegStops");

            migrationBuilder.DropIndex(
                name: "IX_ExecutionLegs_CompanyId",
                table: "ExecutionLegs");

            migrationBuilder.DropIndex(
                name: "IX_ExecutionLegRevisions_CompanyId",
                table: "ExecutionLegRevisions");

            migrationBuilder.DropIndex(
                name: "IX_ExecutionActionReceipts_CompanyId",
                table: "ExecutionActionReceipts");

            migrationBuilder.DropIndex(
                name: "IX_ExecutionActionReceipts_CompanyId_IdempotencyKey",
                table: "ExecutionActionReceipts");

            migrationBuilder.DropIndex(
                name: "IX_Drivers_CompanyId",
                table: "Drivers");

            migrationBuilder.DropIndex(
                name: "IX_Drivers_CompanyId_ExternalId",
                table: "Drivers");

            migrationBuilder.DropIndex(
                name: "IX_DriverHosReadings_CompanyId",
                table: "DriverHosReadings");

            migrationBuilder.DropIndex(
                name: "IX_DispatchWorkspaces_CompanyId",
                table: "DispatchWorkspaces");

            migrationBuilder.DropIndex(
                name: "IX_DispatchWorkspaceRevisions_CompanyId",
                table: "DispatchWorkspaceRevisions");

            migrationBuilder.DropIndex(
                name: "IX_DispatchWorkspaceRevisions_CompanyId_IdempotencyKey",
                table: "DispatchWorkspaceRevisions");

            migrationBuilder.DropIndex(
                name: "IX_DispatchSwitchOperations_CompanyId",
                table: "DispatchSwitchOperations");

            migrationBuilder.DropIndex(
                name: "IX_DispatchSwitchOperations_CompanyId_IdempotencyKey",
                table: "DispatchSwitchOperations");

            migrationBuilder.DropIndex(
                name: "IX_DispatchStops_CompanyId",
                table: "DispatchStops");

            migrationBuilder.DropIndex(
                name: "IX_DispatchStopCompletionEvents_CompanyId",
                table: "DispatchStopCompletionEvents");

            migrationBuilder.DropPrimaryKey(
                name: "PK_DispatchSourceLinks",
                table: "DispatchSourceLinks");

            migrationBuilder.DropIndex(
                name: "IX_DispatchSourceLinks_CompanyId",
                table: "DispatchSourceLinks");

            migrationBuilder.DropIndex(
                name: "IX_DispatchSettings_CompanyId",
                table: "DispatchSettings");

            migrationBuilder.DropIndex(
                name: "IX_DispatchRoutePreviews_CompanyId",
                table: "DispatchRoutePreviews");

            migrationBuilder.DropIndex(
                name: "IX_DispatchRoutePlans_CompanyId",
                table: "DispatchRoutePlans");

            migrationBuilder.DropIndex(
                name: "IX_DispatchRouteChoices_CompanyId",
                table: "DispatchRouteChoices");

            migrationBuilder.DropIndex(
                name: "IX_DispatchRates_CompanyId",
                table: "DispatchRates");

            migrationBuilder.DropIndex(
                name: "IX_DispatchNumberCounters_CompanyId",
                table: "DispatchNumberCounters");

            migrationBuilder.DropIndex(
                name: "IX_DispatchEtaForecasts_CompanyId",
                table: "DispatchEtaForecasts");

            migrationBuilder.DropIndex(
                name: "IX_Dispatches_CompanyId",
                table: "Dispatches");

            migrationBuilder.DropIndex(
                name: "IX_Dispatches_CompanyId_LoadNumber",
                table: "Dispatches");

            migrationBuilder.DropIndex(
                name: "IX_DispatchDocuments_CompanyId",
                table: "DispatchDocuments");

            migrationBuilder.DropIndex(
                name: "IX_DispatchDeadheads_CompanyId",
                table: "DispatchDeadheads");

            migrationBuilder.DropIndex(
                name: "IX_DispatchBaseRoutes_CompanyId",
                table: "DispatchBaseRoutes");

            migrationBuilder.DropIndex(
                name: "IX_DispatchActivityThreads_CompanyId",
                table: "DispatchActivityThreads");

            migrationBuilder.DropIndex(
                name: "IX_DispatchActivityEntries_CompanyId",
                table: "DispatchActivityEntries");

            migrationBuilder.DropIndex(
                name: "IX_Customers_CompanyId",
                table: "Customers");

            migrationBuilder.DropIndex(
                name: "IX_Customers_CompanyId_NormalizedName",
                table: "Customers");

            migrationBuilder.DropIndex(
                name: "IX_BorderSaveReceipts_CompanyId",
                table: "BorderSaveReceipts");

            migrationBuilder.DropIndex(
                name: "IX_BorderCrossings_CompanyId",
                table: "BorderCrossings");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "Trucks");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "TruckPlanningProfiles");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "TruckLocationReadings");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "TruckFuelPlans");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "Trailers");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "TrailerCustodyIntervals");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "SynchronizationCheckpoints");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "SwitchParticipants");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "SourceRoadRequests");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "ShipmentSaveReceipts");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "RouteRecalculationAttempts");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "PlanningRefreshRequests");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "OdometerPositions");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "OdometerIntervals");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "OdometerCaptureCheckpoints");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "Movements");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "MovementDistanceEvidence");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "MovementAllocationEvents");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "MileageCaptureGaps");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "MileageAllocationPolicies");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "LoadExecutionLegs");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "FuelTransactions");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "FuelImportSources");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "FuelDiscounts");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "FleetPlanningSettings");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "ExpenseAttributions");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "ExpenseAttributionEvents");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "ExecutionSourceReceipts");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "ExecutionPlanningChanges");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "ExecutionLegStops");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "ExecutionLegs");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "ExecutionLegRevisions");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "ExecutionActionReceipts");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "Drivers");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "DriverHosReadings");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "DispatchWorkspaces");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "DispatchWorkspaceRevisions");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "DispatchSwitchOperations");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "DispatchStops");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "DispatchStopCompletionEvents");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "DispatchSourceLinks");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "DispatchSettings");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "DispatchRoutePreviews");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "DispatchRoutePlans");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "DispatchRouteChoices");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "DispatchRates");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "DispatchNumberCounters");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "DispatchEtaForecasts");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "Dispatches");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "DispatchDocuments");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "DispatchDeadheads");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "DispatchBaseRoutes");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "DispatchActivityThreads");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "DispatchActivityEntries");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "BorderSaveReceipts");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "BorderCrossings");

            migrationBuilder.AddPrimaryKey(
                name: "PK_DispatchSourceLinks",
                table: "DispatchSourceLinks",
                columns: new[] { "Provider", "ExternalId" });

            migrationBuilder.CreateIndex(
                name: "IX_Trucks_ExternalId",
                table: "Trucks",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Trucks_UnitNumber",
                table: "Trucks",
                column: "UnitNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Trailers_ExternalId",
                table: "Trailers",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Trailers_UnitNumber",
                table: "Trailers",
                column: "UnitNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Movements_IdempotencyKey",
                table: "Movements",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FuelImportSources_GmailMessageId",
                table: "FuelImportSources",
                column: "GmailMessageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_IdempotencyKey",
                table: "Expenses",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionSourceReceipts_IdempotencyKey",
                table: "ExecutionSourceReceipts",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionActionReceipts_IdempotencyKey",
                table: "ExecutionActionReceipts",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Drivers_ExternalId",
                table: "Drivers",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DispatchWorkspaceRevisions_IdempotencyKey",
                table: "DispatchWorkspaceRevisions",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DispatchSwitchOperations_IdempotencyKey",
                table: "DispatchSwitchOperations",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Dispatches_LoadNumber",
                table: "Dispatches",
                column: "LoadNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customers_NormalizedName",
                table: "Customers",
                column: "NormalizedName",
                unique: true);
        }
    }
}
