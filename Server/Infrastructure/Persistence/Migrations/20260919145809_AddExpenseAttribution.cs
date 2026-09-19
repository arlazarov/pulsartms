using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExpenseAttribution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Expenses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Location = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    QuantityUnit = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    TruckId = table.Column<Guid>(type: "uuid", nullable: true),
                    DriverId = table.Column<Guid>(type: "uuid", nullable: true),
                    TrailerId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExecutionLegId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceTruckName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SourceDriverName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceReference = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RecordedBy = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Expenses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Expenses_Drivers_DriverId",
                        column: x => x.DriverId,
                        principalTable: "Drivers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Expenses_Trailers_TrailerId",
                        column: x => x.TrailerId,
                        principalTable: "Trailers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Expenses_Trucks_TruckId",
                        column: x => x.TruckId,
                        principalTable: "Trucks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExpenseAttributionEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttributionId = table.Column<Guid>(type: "uuid", nullable: true),
                    DispatchId = table.Column<Guid>(type: "uuid", nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    PreviousAmount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Basis = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ManualOverride = table.Column<bool>(type: "boolean", nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RecordedBy = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpenseAttributionEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExpenseAttributionEvents_Expenses_ExpenseId",
                        column: x => x.ExpenseId,
                        principalTable: "Expenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExpenseAttributions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    DispatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Basis = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ManualOverride = table.Column<bool>(type: "boolean", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RecordedBy = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpenseAttributions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExpenseAttributions_Dispatches_DispatchId",
                        column: x => x.DispatchId,
                        principalTable: "Dispatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExpenseAttributions_Expenses_ExpenseId",
                        column: x => x.ExpenseId,
                        principalTable: "Expenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseAttributionEvents_ExpenseId_RecordedAt",
                table: "ExpenseAttributionEvents",
                columns: new[] { "ExpenseId", "RecordedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseAttributions_DispatchId",
                table: "ExpenseAttributions",
                column: "DispatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseAttributions_ExpenseId_DispatchId",
                table: "ExpenseAttributions",
                columns: new[] { "ExpenseId", "DispatchId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_DriverId",
                table: "Expenses",
                column: "DriverId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_ExecutionLegId",
                table: "Expenses",
                column: "ExecutionLegId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_IdempotencyKey",
                table: "Expenses",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_Kind_OccurredAt",
                table: "Expenses",
                columns: new[] { "Kind", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_TrailerId",
                table: "Expenses",
                column: "TrailerId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_TruckId_OccurredAt",
                table: "Expenses",
                columns: new[] { "TruckId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExpenseAttributionEvents");

            migrationBuilder.DropTable(
                name: "ExpenseAttributions");

            migrationBuilder.DropTable(
                name: "Expenses");
        }
    }
}
