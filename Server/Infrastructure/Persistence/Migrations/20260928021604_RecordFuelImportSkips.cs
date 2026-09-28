using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecordFuelImportSkips : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FuelImportSkips",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    GmailMessageId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SkippedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FuelImportSkips", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FuelImportSkips_CompanyId",
                table: "FuelImportSkips",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_FuelImportSkips_CompanyId_GmailMessageId",
                table: "FuelImportSkips",
                columns: new[] { "CompanyId", "GmailMessageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FuelImportSkips_CompanyId_SkippedAt",
                table: "FuelImportSkips",
                columns: new[] { "CompanyId", "SkippedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FuelImportSkips");
        }
    }
}
