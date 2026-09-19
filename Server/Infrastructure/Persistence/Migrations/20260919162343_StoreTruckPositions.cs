using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StoreTruckPositions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TruckLocationReadings",
                columns: table => new
                {
                    TruckId = table.Column<Guid>(type: "uuid", nullable: false),
                    Latitude = table.Column<decimal>(type: "numeric(12,8)", precision: 12, scale: 8, nullable: false),
                    Longitude = table.Column<decimal>(type: "numeric(12,8)", precision: 12, scale: 8, nullable: false),
                    Speed = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    Heading = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    EngineState = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FormattedLocation = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    TrailerNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ObservedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FuelPercent = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    FuelObservedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OutsideTemperatureCelsius = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    TemperatureObservedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TruckLocationReadings", x => x.TruckId);
                    table.ForeignKey(
                        name: "FK_TruckLocationReadings_Trucks_TruckId",
                        column: x => x.TruckId,
                        principalTable: "Trucks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TruckLocationReadings_ObservedAt",
                table: "TruckLocationReadings",
                column: "ObservedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TruckLocationReadings");
        }
    }
}
