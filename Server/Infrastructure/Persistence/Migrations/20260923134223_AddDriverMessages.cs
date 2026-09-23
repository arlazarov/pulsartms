using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDriverMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "MessageId",
                table: "FuelVisitSends",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DriverMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    DriverId = table.Column<Guid>(type: "uuid", nullable: false),
                    TruckId = table.Column<Guid>(type: "uuid", nullable: false),
                    DispatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExecutionLegId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssignmentRevision = table.Column<long>(type: "bigint", nullable: false),
                    PlanCalculatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Recipient = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Text = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    VisitKeys = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Attempt = table.Column<int>(type: "integer", nullable: false),
                    ProviderMessageId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    StatusAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ErrorCode = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DriverMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DriverMessages_Drivers_DriverId",
                        column: x => x.DriverId,
                        principalTable: "Drivers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DriverMessages_Trucks_TruckId",
                        column: x => x.TruckId,
                        principalTable: "Trucks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DriverMessagingWindows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Phone = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    LastInboundAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DriverMessagingWindows", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FuelVisitSends_MessageId",
                table: "FuelVisitSends",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_DriverMessages_CompanyId",
                table: "DriverMessages",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_DriverMessages_CompanyId_IdempotencyKey_Attempt",
                table: "DriverMessages",
                columns: new[] { "CompanyId", "IdempotencyKey", "Attempt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DriverMessages_CompanyId_ProviderMessageId",
                table: "DriverMessages",
                columns: new[] { "CompanyId", "ProviderMessageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DriverMessages_DriverId",
                table: "DriverMessages",
                column: "DriverId");

            migrationBuilder.CreateIndex(
                name: "IX_DriverMessages_TruckId_DispatchId",
                table: "DriverMessages",
                columns: new[] { "TruckId", "DispatchId" });

            migrationBuilder.CreateIndex(
                name: "IX_DriverMessagingWindows_CompanyId",
                table: "DriverMessagingWindows",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_DriverMessagingWindows_CompanyId_Channel_Phone",
                table: "DriverMessagingWindows",
                columns: new[] { "CompanyId", "Channel", "Phone" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_FuelVisitSends_DriverMessages_MessageId",
                table: "FuelVisitSends",
                column: "MessageId",
                principalTable: "DriverMessages",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FuelVisitSends_DriverMessages_MessageId",
                table: "FuelVisitSends");

            migrationBuilder.DropTable(
                name: "DriverMessages");

            migrationBuilder.DropTable(
                name: "DriverMessagingWindows");

            migrationBuilder.DropIndex(
                name: "IX_FuelVisitSends_MessageId",
                table: "FuelVisitSends");

            migrationBuilder.DropColumn(
                name: "MessageId",
                table: "FuelVisitSends");
        }
    }
}
