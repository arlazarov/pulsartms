using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StoreRouteChunks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GeometryManifestJson",
                table: "DispatchRoutePlans",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "GeometryRevision",
                table: "DispatchRoutePlans",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "RouteGeometryChanges",
                columns: table => new
                {
                    RoutePlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ChangesJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RouteGeometryChanges", x => new { x.RoutePlanId, x.Revision });
                    table.ForeignKey(
                        name: "FK_RouteGeometryChanges_DispatchRoutePlans_RoutePlanId",
                        column: x => x.RoutePlanId,
                        principalTable: "DispatchRoutePlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RouteGeometryChunks",
                columns: table => new
                {
                    RoutePlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CoordinatesJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RouteGeometryChunks", x => new { x.RoutePlanId, x.Key });
                    table.ForeignKey(
                        name: "FK_RouteGeometryChunks_DispatchRoutePlans_RoutePlanId",
                        column: x => x.RoutePlanId,
                        principalTable: "DispatchRoutePlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RouteGeometryChanges_CompanyId",
                table: "RouteGeometryChanges",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_RouteGeometryChunks_CompanyId",
                table: "RouteGeometryChunks",
                column: "CompanyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM "DispatchRoutePlans"
                    WHERE "GeometryManifestJson" IS NOT NULL) THEN
                    RAISE EXCEPTION 'Cannot discard referenced route chunks';
                  END IF;
                END $$;
                """);

            migrationBuilder.DropTable(
                name: "RouteGeometryChanges");

            migrationBuilder.DropTable(
                name: "RouteGeometryChunks");

            migrationBuilder.DropColumn(
                name: "GeometryManifestJson",
                table: "DispatchRoutePlans");

            migrationBuilder.DropColumn(
                name: "GeometryRevision",
                table: "DispatchRoutePlans");
        }
    }
}
