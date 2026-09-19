using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
  /// <inheritdoc />
  public partial class AddRouteRecalculationBudget : Migration
  {
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.CreateTable(
        name: "RouteRecalculationAttempts",
        columns: table => new
        {
          Id = table.Column<Guid>(type: "uuid", nullable: false),
          TruckId = table.Column<Guid>(type: "uuid", nullable: false),
          CreatedAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: false
          ),
          Latitude = table.Column<double>(
            type: "double precision",
            nullable: false
          ),
          Longitude = table.Column<double>(
            type: "double precision",
            nullable: false
          ),
        },
        constraints: table =>
        {
          table.PrimaryKey("PK_RouteRecalculationAttempts", x => x.Id);
        }
      );

      migrationBuilder.CreateIndex(
        name: "IX_RouteRecalculationAttempts_TruckId_CreatedAt",
        table: "RouteRecalculationAttempts",
        columns: new[] { "TruckId", "CreatedAt" }
      );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.DropTable(name: "RouteRecalculationAttempts");
    }
  }
}
