using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDriverInbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Conversations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    BusinessNumberId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Participant = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DriverId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastInboundAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastMessageAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastPreview = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LastMessageId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClaimedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ClaimedUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Conversations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ConversationMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    BusinessNumberId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Direction = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Body = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    AuthorId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReplyToId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProviderMessageId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IdempotencyKey = table.Column<Guid>(type: "uuid", nullable: true),
                    Attempt = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    StatusAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ErrorCode = table.Column<int>(type: "integer", nullable: true),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConversationMessages_Conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "Conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ConversationReads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReadThrough = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationReads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConversationReads_Conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "Conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MessageAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    StoredFileId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProviderMediaId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    MediaExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeclaredType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    OriginalName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Caption = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LeaseToken = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessageAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MessageAttachments_ConversationMessages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "ConversationMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConversationMessages_CompanyId",
                table: "ConversationMessages",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationMessages_CompanyId_Channel_BusinessNumberId_Ide~",
                table: "ConversationMessages",
                columns: new[] { "CompanyId", "Channel", "BusinessNumberId", "IdempotencyKey", "Attempt" },
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationMessages_CompanyId_Channel_BusinessNumberId_Pro~",
                table: "ConversationMessages",
                columns: new[] { "CompanyId", "Channel", "BusinessNumberId", "ProviderMessageId" },
                unique: true,
                filter: "\"ProviderMessageId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationMessages_ConversationId_SentAt",
                table: "ConversationMessages",
                columns: new[] { "ConversationId", "SentAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ConversationReads_CompanyId",
                table: "ConversationReads",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationReads_CompanyId_ConversationId_UserId",
                table: "ConversationReads",
                columns: new[] { "CompanyId", "ConversationId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConversationReads_ConversationId",
                table: "ConversationReads",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_CompanyId",
                table: "Conversations",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_CompanyId_Channel_BusinessNumberId_Participant",
                table: "Conversations",
                columns: new[] { "CompanyId", "Channel", "BusinessNumberId", "Participant" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_CompanyId_LastMessageAt",
                table: "Conversations",
                columns: new[] { "CompanyId", "LastMessageAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MessageAttachments_CompanyId",
                table: "MessageAttachments",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_MessageAttachments_MessageId",
                table: "MessageAttachments",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_MessageAttachments_State_NextAttemptAt",
                table: "MessageAttachments",
                columns: new[] { "State", "NextAttemptAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConversationReads");

            migrationBuilder.DropTable(
                name: "MessageAttachments");

            migrationBuilder.DropTable(
                name: "ConversationMessages");

            migrationBuilder.DropTable(
                name: "Conversations");
        }
    }
}
