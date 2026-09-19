using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StoreDriverHosReadings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DriverHosReadings",
                columns: table => new
                {
                    DriverExternalId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BreakMs = table.Column<long>(type: "bigint", nullable: true),
                    DriveMs = table.Column<long>(type: "bigint", nullable: true),
                    ShiftMs = table.Column<long>(type: "bigint", nullable: true),
                    CycleMs = table.Column<long>(type: "bigint", nullable: true),
                    CurrentDutyStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ObservedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DriverHosReadings", x => x.DriverExternalId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DriverHosReadings");
        }
    }
}
