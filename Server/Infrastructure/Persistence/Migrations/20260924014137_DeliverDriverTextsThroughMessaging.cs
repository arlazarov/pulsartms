using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DeliverDriverTextsThroughMessaging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DriverMessagingWindows");

            migrationBuilder.AddColumn<string>(
                name: "BusinessNumberId",
                table: "DriverMessages",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BusinessNumberId",
                table: "DriverMessages");

            migrationBuilder.CreateTable(
                name: "DriverMessagingWindows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    LastInboundAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Phone = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DriverMessagingWindows", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DriverMessagingWindows_CompanyId",
                table: "DriverMessagingWindows",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_DriverMessagingWindows_CompanyId_Channel_Phone",
                table: "DriverMessagingWindows",
                columns: new[] { "CompanyId", "Channel", "Phone" },
                unique: true);
        }
    }
}
