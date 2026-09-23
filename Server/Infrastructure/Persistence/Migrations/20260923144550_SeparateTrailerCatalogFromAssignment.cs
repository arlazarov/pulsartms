using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeparateTrailerCatalogFromAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Trailers_CompanyId_ExternalId",
                table: "Trailers");

            migrationBuilder.AddColumn<Guid>(
                name: "TelemetryTrailerId",
                table: "Trucks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TelemetryTrailerKnown",
                table: "Trucks",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "TrailerConflictId",
                table: "Trucks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TrailerSource",
                table: "Trucks",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "Trailers",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            // Until now a truck's trailer came only from the telemetry
            // provider, so what is there is its last word.
            migrationBuilder.Sql(
                "UPDATE \"Trucks\" SET \"TelemetryTrailerKnown\" = TRUE, "
                + "\"TelemetryTrailerId\" = \"TrailerId\", "
                + "\"TrailerSource\" = 'telemetry' "
                + "WHERE \"TrailerId\" IS NOT NULL;");

            migrationBuilder.CreateIndex(
                name: "IX_Trucks_TelemetryTrailerId",
                table: "Trucks",
                column: "TelemetryTrailerId");

            migrationBuilder.CreateIndex(
                name: "IX_Trucks_TrailerConflictId",
                table: "Trucks",
                column: "TrailerConflictId");

            migrationBuilder.CreateIndex(
                name: "IX_Trailers_CompanyId_ExternalId",
                table: "Trailers",
                columns: new[] { "CompanyId", "ExternalId" },
                unique: true,
                filter: "\"ExternalId\" <> ''");

            migrationBuilder.AddForeignKey(
                name: "FK_Trucks_Trailers_TelemetryTrailerId",
                table: "Trucks",
                column: "TelemetryTrailerId",
                principalTable: "Trailers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Trucks_Trailers_TrailerConflictId",
                table: "Trucks",
                column: "TrailerConflictId",
                principalTable: "Trailers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Trucks_Trailers_TelemetryTrailerId",
                table: "Trucks");

            migrationBuilder.DropForeignKey(
                name: "FK_Trucks_Trailers_TrailerConflictId",
                table: "Trucks");

            migrationBuilder.DropIndex(
                name: "IX_Trucks_TelemetryTrailerId",
                table: "Trucks");

            migrationBuilder.DropIndex(
                name: "IX_Trucks_TrailerConflictId",
                table: "Trucks");

            migrationBuilder.DropIndex(
                name: "IX_Trailers_CompanyId_ExternalId",
                table: "Trailers");

            migrationBuilder.DropColumn(
                name: "TelemetryTrailerId",
                table: "Trucks");

            migrationBuilder.DropColumn(
                name: "TelemetryTrailerKnown",
                table: "Trucks");

            migrationBuilder.DropColumn(
                name: "TrailerConflictId",
                table: "Trucks");

            migrationBuilder.DropColumn(
                name: "TrailerSource",
                table: "Trucks");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "Trailers");

            migrationBuilder.CreateIndex(
                name: "IX_Trailers_CompanyId_ExternalId",
                table: "Trailers",
                columns: new[] { "CompanyId", "ExternalId" },
                unique: true);
        }
    }
}
