using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ScopeCarrierNaturalKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_DriverHosReadings",
                table: "DriverHosReadings");

            migrationBuilder.DropPrimaryKey(
                name: "PK_DispatchNumberCounters",
                table: "DispatchNumberCounters");

            migrationBuilder.AddPrimaryKey(
                name: "PK_DriverHosReadings",
                table: "DriverHosReadings",
                columns: new[] { "CompanyId", "DriverExternalId" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_DispatchNumberCounters",
                table: "DispatchNumberCounters",
                columns: new[] { "CompanyId", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_DriverHosReadings",
                table: "DriverHosReadings");

            migrationBuilder.DropPrimaryKey(
                name: "PK_DispatchNumberCounters",
                table: "DispatchNumberCounters");

            migrationBuilder.AddPrimaryKey(
                name: "PK_DriverHosReadings",
                table: "DriverHosReadings",
                column: "DriverExternalId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_DispatchNumberCounters",
                table: "DispatchNumberCounters",
                column: "Id");
        }
    }
}
