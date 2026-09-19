using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
  public partial class AddRoutePlanning : Migration
  {
    protected override void Up(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.CreateTable(
        name: "DispatchRoutePlans",
        columns: table => new
        {
          Id = table.Column<Guid>(type: "uuid", nullable: false),
          DispatchId = table.Column<Guid>(type: "uuid", nullable: false),
          TruckId = table.Column<Guid>(type: "uuid", nullable: false),
          InputHash = table.Column<string>(
            type: "character varying(64)",
            maxLength: 64,
            nullable: false
          ),
          PlanJson = table.Column<string>(type: "text", nullable: false),
          CreatedAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: false
          ),
        },
        constraints: table =>
        {
          table.PrimaryKey("PK_DispatchRoutePlans", x => x.Id);
          table.ForeignKey(
            name: "FK_DispatchRoutePlans_Dispatches_DispatchId",
            column: x => x.DispatchId,
            principalTable: "Dispatches",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade
          );
          table.ForeignKey(
            name: "FK_DispatchRoutePlans_Trucks_TruckId",
            column: x => x.TruckId,
            principalTable: "Trucks",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade
          );
        }
      );

      migrationBuilder.CreateTable(
        name: "RoutingApiCalls",
        columns: table => new
        {
          Id = table.Column<Guid>(type: "uuid", nullable: false),
          RequestHash = table.Column<string>(
            type: "character varying(64)",
            maxLength: 64,
            nullable: false
          ),
          Operation = table.Column<string>(
            type: "character varying(30)",
            maxLength: 30,
            nullable: false
          ),
          CreatedAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: false
          ),
          ExpiresAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: false
          ),
          ResultJson = table.Column<string>(type: "text", nullable: true),
        },
        constraints: table =>
        {
          table.PrimaryKey("PK_RoutingApiCalls", x => x.Id);
        }
      );

      migrationBuilder.CreateTable(
        name: "TruckPlanningProfiles",
        columns: table => new
        {
          Id = table.Column<Guid>(type: "uuid", nullable: false),
          TruckId = table.Column<Guid>(type: "uuid", nullable: false),
          SettingsJson = table.Column<string>(type: "text", nullable: false),
        },
        constraints: table =>
        {
          table.PrimaryKey("PK_TruckPlanningProfiles", x => x.Id);
          table.ForeignKey(
            name: "FK_TruckPlanningProfiles_Trucks_TruckId",
            column: x => x.TruckId,
            principalTable: "Trucks",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade
          );
        }
      );

      migrationBuilder.CreateIndex(
        name: "IX_DispatchRoutePlans_DispatchId",
        table: "DispatchRoutePlans",
        column: "DispatchId",
        unique: true
      );

      migrationBuilder.CreateIndex(
        name: "IX_DispatchRoutePlans_TruckId",
        table: "DispatchRoutePlans",
        column: "TruckId"
      );

      migrationBuilder.CreateIndex(
        name: "IX_RoutingApiCalls_CreatedAt",
        table: "RoutingApiCalls",
        column: "CreatedAt"
      );

      migrationBuilder.CreateIndex(
        name: "IX_RoutingApiCalls_RequestHash_CreatedAt",
        table: "RoutingApiCalls",
        columns: new[] { "RequestHash", "CreatedAt" }
      );

      migrationBuilder.CreateIndex(
        name: "IX_TruckPlanningProfiles_TruckId",
        table: "TruckPlanningProfiles",
        column: "TruckId",
        unique: true
      );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.DropTable(name: "DispatchRoutePlans");

      migrationBuilder.DropTable(name: "RoutingApiCalls");

      migrationBuilder.DropTable(name: "TruckPlanningProfiles");
    }
  }
}
