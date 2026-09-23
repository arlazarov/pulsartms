using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFuelVisitSendsAndAutomaticFuelSending : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutomaticFuelSending",
                table: "DispatchSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "FuelVisitSends",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    TruckId = table.Column<Guid>(type: "uuid", nullable: false),
                    DispatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExecutionLegId = table.Column<Guid>(type: "uuid", nullable: true),
                    ScopeId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignmentRevision = table.Column<long>(type: "bigint", nullable: false),
                    StationId = table.Column<Guid>(type: "uuid", nullable: false),
                    BeforeStopId = table.Column<Guid>(type: "uuid", nullable: false),
                    Content = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    PlanCalculatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SentBy = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FuelVisitSends", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FuelVisitSends_Dispatches_DispatchId",
                        column: x => x.DispatchId,
                        principalTable: "Dispatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FuelVisitSends_Trucks_TruckId",
                        column: x => x.TruckId,
                        principalTable: "Trucks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FuelVisitSends_CompanyId",
                table: "FuelVisitSends",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_FuelVisitSends_CompanyId_TruckId_ScopeId_AssignmentRevision~",
                table: "FuelVisitSends",
                columns: new[] { "CompanyId", "TruckId", "ScopeId", "AssignmentRevision", "StationId", "BeforeStopId", "Content" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FuelVisitSends_DispatchId",
                table: "FuelVisitSends",
                column: "DispatchId");

            migrationBuilder.CreateIndex(
                name: "IX_FuelVisitSends_TruckId_DispatchId",
                table: "FuelVisitSends",
                columns: new[] { "TruckId", "DispatchId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FuelVisitSends");

            migrationBuilder.DropColumn(
                name: "AutomaticFuelSending",
                table: "DispatchSettings");
        }
    }
}
