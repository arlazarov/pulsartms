using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StopDriverAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CoDriverId",
                table: "ExecutionLegStops",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DriverId",
                table: "ExecutionLegStops",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HasDriverOverride",
                table: "ExecutionLegStops",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM "ExecutionLegStops"
                    WHERE "HasDriverOverride") THEN
                    RAISE EXCEPTION 'Driver assignments must be reconciled before downgrade.';
                  END IF;
                END $$;
                """);
            migrationBuilder.DropColumn(
                name: "CoDriverId",
                table: "ExecutionLegStops");

            migrationBuilder.DropColumn(
                name: "DriverId",
                table: "ExecutionLegStops");

            migrationBuilder.DropColumn(
                name: "HasDriverOverride",
                table: "ExecutionLegStops");
        }
    }
}
