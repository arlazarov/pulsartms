using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class KeepEarlyDeliveryStatuses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PendingDeliveryStatuses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    BusinessNumberId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProviderMessageId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    At = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ErrorCode = table.Column<int>(type: "integer", nullable: true),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingDeliveryStatuses", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PendingDeliveryStatuses_CompanyId",
                table: "PendingDeliveryStatuses",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_PendingDeliveryStatuses_CompanyId_Channel_BusinessNumberId_~",
                table: "PendingDeliveryStatuses",
                columns: new[] { "CompanyId", "Channel", "BusinessNumberId", "ProviderMessageId", "Status" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PendingDeliveryStatuses_CompanyId_ReceivedAt",
                table: "PendingDeliveryStatuses",
                columns: new[] { "CompanyId", "ReceivedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PendingDeliveryStatuses");
        }
    }
}
