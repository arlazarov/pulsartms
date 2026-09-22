using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecordRouteMovement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RouteMovementChunks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoutePlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    TruckId = table.Column<Guid>(type: "uuid", nullable: false),
                    From = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    To = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    MovementJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RouteMovementChunks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RouteMovementChunks_DispatchRoutePlans_RoutePlanId",
                        column: x => x.RoutePlanId,
                        principalTable: "DispatchRoutePlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RouteMovementChunks_CompanyId",
                table: "RouteMovementChunks",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_RouteMovementChunks_CompanyId_TruckId_To",
                table: "RouteMovementChunks",
                columns: new[] { "CompanyId", "TruckId", "To" });

            migrationBuilder.CreateIndex(
                name: "IX_RouteMovementChunks_RoutePlanId",
                table: "RouteMovementChunks",
                column: "RoutePlanId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM "RouteMovementChunks") THEN
                    RAISE EXCEPTION 'Cannot discard recorded route movement';
                  END IF;
                END $$;
                """);

            migrationBuilder.DropTable(
                name: "RouteMovementChunks");
        }
    }
}
