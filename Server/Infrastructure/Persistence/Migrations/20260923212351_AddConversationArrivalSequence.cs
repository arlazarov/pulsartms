using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationArrivalSequence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "LastInboundSequence",
                table: "Conversations",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "ConversationArrivalHeads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationArrivalHeads", x => x.Id);
                });

            // Existing arrivals are numbered per company in the order they
            // were recorded, and each company's head starts at its highest
            // number, before the arrival times that give that order go.
            migrationBuilder.Sql(
                """
                UPDATE "Conversations" AS c
                SET "LastInboundSequence" = ordered.number
                FROM (
                  SELECT "Id", row_number() OVER (
                    PARTITION BY "CompanyId"
                    ORDER BY "LastInboundArrivedAt", "Id") AS number
                  FROM "Conversations"
                  WHERE "LastInboundRevision" > 0
                ) AS ordered
                WHERE c."Id" = ordered."Id";
                INSERT INTO "ConversationArrivalHeads"
                  ("Id", "CompanyId", "Sequence")
                SELECT gen_random_uuid(), "CompanyId",
                  max("LastInboundSequence")
                FROM "Conversations"
                WHERE "LastInboundSequence" > 0
                GROUP BY "CompanyId";
                """);

            migrationBuilder.DropIndex(
                name: "IX_Conversations_CompanyId_LastInboundArrivedAt",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "LastInboundArrivedAt",
                table: "Conversations");

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_CompanyId_LastInboundSequence",
                table: "Conversations",
                columns: new[] { "CompanyId", "LastInboundSequence" });

            migrationBuilder.CreateIndex(
                name: "IX_ConversationArrivalHeads_CompanyId",
                table: "ConversationArrivalHeads",
                column: "CompanyId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConversationArrivalHeads");

            migrationBuilder.DropIndex(
                name: "IX_Conversations_CompanyId_LastInboundSequence",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "LastInboundSequence",
                table: "Conversations");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastInboundArrivedAt",
                table: "Conversations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_CompanyId_LastInboundArrivedAt",
                table: "Conversations",
                columns: new[] { "CompanyId", "LastInboundArrivedAt" });
        }
    }
}
