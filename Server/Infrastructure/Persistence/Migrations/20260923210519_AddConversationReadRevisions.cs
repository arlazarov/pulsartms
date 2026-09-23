using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationReadRevisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReadThrough",
                table: "ConversationReads");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastInboundArrivedAt",
                table: "Conversations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LastInboundRevision",
                table: "Conversations",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "ReadRevision",
                table: "ConversationReads",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "ArrivedRevision",
                table: "ConversationMessages",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            // Read markers were times of the provider's clock and are not
            // carried over: every conversation shows unread once. Arrival
            // revisions start at each conversation's current revision.
            migrationBuilder.Sql(
                """
                UPDATE "Conversations"
                SET "LastInboundRevision" = "Revision",
                    "LastInboundArrivedAt" = "LastInboundAt"
                WHERE "LastInboundAt" IS NOT NULL;
                UPDATE "ConversationMessages" AS m
                SET "ArrivedRevision" = c."Revision"
                FROM "Conversations" AS c
                WHERE m."ConversationId" = c."Id" AND m."Direction" = 'in';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_CompanyId_LastInboundArrivedAt",
                table: "Conversations",
                columns: new[] { "CompanyId", "LastInboundArrivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ConversationMessages_ConversationId_ArrivedRevision",
                table: "ConversationMessages",
                columns: new[] { "ConversationId", "ArrivedRevision" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Conversations_CompanyId_LastInboundArrivedAt",
                table: "Conversations");

            migrationBuilder.DropIndex(
                name: "IX_ConversationMessages_ConversationId_ArrivedRevision",
                table: "ConversationMessages");

            migrationBuilder.DropColumn(
                name: "LastInboundArrivedAt",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "LastInboundRevision",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "ReadRevision",
                table: "ConversationReads");

            migrationBuilder.DropColumn(
                name: "ArrivedRevision",
                table: "ConversationMessages");

            migrationBuilder.AddColumn<DateTime>(
                name: "ReadThrough",
                table: "ConversationReads",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));
        }
    }
}
