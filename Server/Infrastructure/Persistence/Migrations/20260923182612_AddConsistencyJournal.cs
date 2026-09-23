using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConsistencyJournal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConsistencyEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    FindingId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Rule = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    EntityKey = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Occurrence = table.Column<int>(type: "integer", nullable: false),
                    PassId = table.Column<Guid>(type: "uuid", nullable: false),
                    At = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DetailJson = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsistencyEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ConsistencyFindings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Rule = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    RuleVersion = table.Column<int>(type: "integer", nullable: false),
                    EntityKey = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Occurrence = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Condition = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Versions = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    EvidenceJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    FirstSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastTransitionAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RepairAttempts = table.Column<int>(type: "integer", nullable: false),
                    LastRepairAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastRepairOutcome = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Escalated = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsistencyFindings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ConsistencyIncidents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Rule = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    EntityKey = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Reason = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Recurrences = table.Column<int>(type: "integer", nullable: false),
                    LastFindingId = table.Column<Guid>(type: "uuid", nullable: false),
                    OpenedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsistencyIncidents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ConsistencyJournalHeads",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    LastSequence = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsistencyJournalHeads", x => x.CompanyId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConsistencyEvents_CompanyId",
                table: "ConsistencyEvents",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_ConsistencyEvents_CompanyId_Sequence",
                table: "ConsistencyEvents",
                columns: new[] { "CompanyId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConsistencyEvents_FindingId",
                table: "ConsistencyEvents",
                column: "FindingId");

            migrationBuilder.CreateIndex(
                name: "IX_ConsistencyFindings_CompanyId",
                table: "ConsistencyFindings",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_ConsistencyFindings_CompanyId_Rule_EntityKey_Occurrence",
                table: "ConsistencyFindings",
                columns: new[] { "CompanyId", "Rule", "EntityKey", "Occurrence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConsistencyFindings_CompanyId_State_Rule",
                table: "ConsistencyFindings",
                columns: new[] { "CompanyId", "State", "Rule" });

            migrationBuilder.CreateIndex(
                name: "IX_ConsistencyFindings_Open",
                table: "ConsistencyFindings",
                columns: new[] { "CompanyId", "Rule", "EntityKey" },
                unique: true,
                filter: "\"State\" = 'open'");

            migrationBuilder.CreateIndex(
                name: "IX_ConsistencyIncidents_CompanyId",
                table: "ConsistencyIncidents",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_ConsistencyIncidents_CompanyId_LastAt",
                table: "ConsistencyIncidents",
                columns: new[] { "CompanyId", "LastAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ConsistencyIncidents_CompanyId_Rule_EntityKey",
                table: "ConsistencyIncidents",
                columns: new[] { "CompanyId", "Rule", "EntityKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConsistencyJournalHeads_CompanyId",
                table: "ConsistencyJournalHeads",
                column: "CompanyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConsistencyEvents");

            migrationBuilder.DropTable(
                name: "ConsistencyFindings");

            migrationBuilder.DropTable(
                name: "ConsistencyIncidents");

            migrationBuilder.DropTable(
                name: "ConsistencyJournalHeads");
        }
    }
}
