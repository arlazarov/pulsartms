using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FreeTheServerCheckpointsFromCarriers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SynchronizationCheckpoints_CompanyId",
                table: "SynchronizationCheckpoints");

            migrationBuilder.DropIndex(
                name: "IX_OdometerCaptureCheckpoints_CompanyId",
                table: "OdometerCaptureCheckpoints");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "SynchronizationCheckpoints");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "OdometerCaptureCheckpoints");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "SynchronizationCheckpoints",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "OdometerCaptureCheckpoints",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_SynchronizationCheckpoints_CompanyId",
                table: "SynchronizationCheckpoints",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_OdometerCaptureCheckpoints_CompanyId",
                table: "OdometerCaptureCheckpoints",
                column: "CompanyId");
        }
    }
}
