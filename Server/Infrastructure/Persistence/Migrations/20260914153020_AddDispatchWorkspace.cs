using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
  /// <inheritdoc />
  public partial class AddDispatchWorkspace : Migration
  {
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.AddColumn<string>(
        name: "AppointmentTimeZoneId",
        table: "DispatchStops",
        type: "character varying(100)",
        maxLength: 100,
        nullable: false,
        defaultValue: ""
      );

      migrationBuilder.CreateTable(
        name: "DispatchActivityThreads",
        columns: table => new
        {
          Id = table.Column<Guid>(type: "uuid", nullable: false),
          Revision = table.Column<long>(type: "bigint", nullable: false),
        },
        constraints: table =>
        {
          table.PrimaryKey("PK_DispatchActivityThreads", x => x.Id);
          table.ForeignKey(
            name: "FK_DispatchActivityThreads_Dispatches_Id",
            column: x => x.Id,
            principalTable: "Dispatches",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict
          );
        }
      );

      migrationBuilder.CreateTable(
        name: "DispatchDocuments",
        columns: table => new
        {
          Id = table.Column<Guid>(type: "uuid", nullable: false),
          DispatchId = table.Column<Guid>(type: "uuid", nullable: false),
          Kind = table.Column<string>(
            type: "character varying(16)",
            maxLength: 16,
            nullable: false
          ),
          FileName = table.Column<string>(
            type: "character varying(180)",
            maxLength: 180,
            nullable: false
          ),
          ContentType = table.Column<string>(
            type: "character varying(64)",
            maxLength: 64,
            nullable: false
          ),
          Content = table.Column<byte[]>(type: "bytea", nullable: false),
          Length = table.Column<int>(type: "integer", nullable: false),
          ContentHash = table.Column<string>(
            type: "character varying(64)",
            maxLength: 64,
            nullable: false
          ),
          RecordedAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: false
          ),
          RecordedBy = table.Column<Guid>(type: "uuid", nullable: false),
        },
        constraints: table =>
        {
          table.PrimaryKey("PK_DispatchDocuments", x => x.Id);
          table.ForeignKey(
            name: "FK_DispatchDocuments_Dispatches_DispatchId",
            column: x => x.DispatchId,
            principalTable: "Dispatches",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict
          );
        }
      );

      migrationBuilder.CreateTable(
        name: "DispatchWorkspaceRevisions",
        columns: table => new
        {
          Id = table.Column<Guid>(type: "uuid", nullable: false),
          DispatchId = table.Column<Guid>(type: "uuid", nullable: false),
          Revision = table.Column<long>(type: "bigint", nullable: false),
          IdempotencyKey = table.Column<Guid>(type: "uuid", nullable: false),
          RequestHash = table.Column<string>(
            type: "character varying(64)",
            maxLength: 64,
            nullable: false
          ),
          SnapshotJson = table.Column<string>(type: "text", nullable: false),
          Summary = table.Column<string>(
            type: "character varying(500)",
            maxLength: 500,
            nullable: false
          ),
          RecordedAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: false
          ),
          RecordedBy = table.Column<Guid>(type: "uuid", nullable: false),
          ActorName = table.Column<string>(
            type: "character varying(200)",
            maxLength: 200,
            nullable: false
          ),
        },
        constraints: table =>
        {
          table.PrimaryKey("PK_DispatchWorkspaceRevisions", x => x.Id);
          table.ForeignKey(
            name: "FK_DispatchWorkspaceRevisions_Dispatches_DispatchId",
            column: x => x.DispatchId,
            principalTable: "Dispatches",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict
          );
        }
      );

      migrationBuilder.CreateTable(
        name: "DispatchWorkspaces",
        columns: table => new
        {
          Id = table.Column<Guid>(type: "uuid", nullable: false),
          Revision = table.Column<long>(type: "bigint", nullable: false),
          MetadataJson = table.Column<string>(type: "text", nullable: false),
          StopExtrasJson = table.Column<string>(type: "text", nullable: false),
          OwnsStops = table.Column<bool>(type: "boolean", nullable: false),
          OwnsCommercial = table.Column<bool>(type: "boolean", nullable: false),
          SourceStopsJson = table.Column<string>(type: "text", nullable: false),
          SourceCommercialJson = table.Column<string>(
            type: "text",
            nullable: false
          ),
          SourceReviewReason = table.Column<string>(
            type: "character varying(1000)",
            maxLength: 1000,
            nullable: true
          ),
          RecordedAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: false
          ),
          RecordedBy = table.Column<Guid>(type: "uuid", nullable: false),
        },
        constraints: table =>
        {
          table.PrimaryKey("PK_DispatchWorkspaces", x => x.Id);
          table.ForeignKey(
            name: "FK_DispatchWorkspaces_Dispatches_Id",
            column: x => x.Id,
            principalTable: "Dispatches",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict
          );
        }
      );

      migrationBuilder.CreateTable(
        name: "DispatchActivityEntries",
        columns: table => new
        {
          Id = table.Column<Guid>(type: "uuid", nullable: false),
          DispatchId = table.Column<Guid>(type: "uuid", nullable: false),
          CreatedRevision = table.Column<long>(type: "bigint", nullable: false),
          Revision = table.Column<long>(type: "bigint", nullable: false),
          AddOperationId = table.Column<Guid>(type: "uuid", nullable: false),
          Kind = table.Column<string>(
            type: "character varying(30)",
            maxLength: 30,
            nullable: false
          ),
          Text = table.Column<string>(
            type: "character varying(4000)",
            maxLength: 4000,
            nullable: false
          ),
          StopId = table.Column<Guid>(type: "uuid", nullable: true),
          StopLabel = table.Column<string>(
            type: "character varying(1000)",
            maxLength: 1000,
            nullable: true
          ),
          DriverId = table.Column<Guid>(type: "uuid", nullable: true),
          DriverName = table.Column<string>(
            type: "character varying(200)",
            maxLength: 200,
            nullable: true
          ),
          ActorId = table.Column<Guid>(type: "uuid", nullable: false),
          ActorName = table.Column<string>(
            type: "character varying(200)",
            maxLength: 200,
            nullable: false
          ),
          RecordedAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: false
          ),
          NeedsAttention = table.Column<bool>(type: "boolean", nullable: false),
          ResolveOperationId = table.Column<Guid>(type: "uuid", nullable: true),
          ResolvedBy = table.Column<Guid>(type: "uuid", nullable: true),
          ResolvedByName = table.Column<string>(
            type: "character varying(200)",
            maxLength: 200,
            nullable: true
          ),
          ResolvedAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: true
          ),
        },
        constraints: table =>
        {
          table.PrimaryKey("PK_DispatchActivityEntries", x => x.Id);
          table.ForeignKey(
            name: "FK_DispatchActivityEntries_DispatchActivityThreads_DispatchId",
            column: x => x.DispatchId,
            principalTable: "DispatchActivityThreads",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict
          );
        }
      );

      migrationBuilder.CreateIndex(
        name: "IX_DispatchActivityEntries_DispatchId_AddOperationId",
        table: "DispatchActivityEntries",
        columns: new[] { "DispatchId", "AddOperationId" },
        unique: true
      );

      migrationBuilder.CreateIndex(
        name: "IX_DispatchActivityEntries_DispatchId_CreatedRevision",
        table: "DispatchActivityEntries",
        columns: new[] { "DispatchId", "CreatedRevision" },
        unique: true
      );

      migrationBuilder.CreateIndex(
        name: "IX_DispatchActivityEntries_DispatchId_NeedsAttention_ResolvedA~",
        table: "DispatchActivityEntries",
        columns: new[]
        {
          "DispatchId",
          "NeedsAttention",
          "ResolvedAt",
          "CreatedRevision",
        }
      );

      migrationBuilder.CreateIndex(
        name: "IX_DispatchDocuments_DispatchId_RecordedAt",
        table: "DispatchDocuments",
        columns: new[] { "DispatchId", "RecordedAt" }
      );

      migrationBuilder.CreateIndex(
        name: "IX_DispatchWorkspaceRevisions_DispatchId_Revision",
        table: "DispatchWorkspaceRevisions",
        columns: new[] { "DispatchId", "Revision" },
        unique: true
      );

      migrationBuilder.CreateIndex(
        name: "IX_DispatchWorkspaceRevisions_IdempotencyKey",
        table: "DispatchWorkspaceRevisions",
        column: "IdempotencyKey",
        unique: true
      );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.DropTable(name: "DispatchActivityEntries");

      migrationBuilder.DropTable(name: "DispatchDocuments");

      migrationBuilder.DropTable(name: "DispatchWorkspaceRevisions");

      migrationBuilder.DropTable(name: "DispatchWorkspaces");

      migrationBuilder.DropTable(name: "DispatchActivityThreads");

      migrationBuilder.DropColumn(
        name: "AppointmentTimeZoneId",
        table: "DispatchStops"
      );
    }
  }
}
