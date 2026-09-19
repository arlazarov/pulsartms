using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
  /// <inheritdoc />
  public partial class StoreTruckFuelPlans : Migration
  {
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.CreateTable(
        name: "TruckFuelPlans",
        columns: table => new
        {
          Id = table.Column<Guid>(type: "uuid", nullable: false),
          TruckId = table.Column<Guid>(type: "uuid", nullable: false),
          RootDispatchId = table.Column<Guid>(type: "uuid", nullable: false),
          CalculatedAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: false
          ),
          SummaryJson = table.Column<string>(type: "text", nullable: false),
          CheckedRouteJson = table.Column<string>(type: "text", nullable: true),
        },
        constraints: table =>
        {
          table.PrimaryKey("PK_TruckFuelPlans", x => x.Id);
          table.ForeignKey(
            name: "FK_TruckFuelPlans_Dispatches_RootDispatchId",
            column: x => x.RootDispatchId,
            principalTable: "Dispatches",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade
          );
          table.ForeignKey(
            name: "FK_TruckFuelPlans_Trucks_TruckId",
            column: x => x.TruckId,
            principalTable: "Trucks",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade
          );
        }
      );

      migrationBuilder.CreateIndex(
        name: "IX_TruckFuelPlans_RootDispatchId",
        table: "TruckFuelPlans",
        column: "RootDispatchId"
      );

      migrationBuilder.CreateIndex(
        name: "IX_TruckFuelPlans_TruckId",
        table: "TruckFuelPlans",
        column: "TruckId",
        unique: true
      );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.DropTable(name: "TruckFuelPlans");
    }
  }
}
