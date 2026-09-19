using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
  /// <inheritdoc />
  public partial class AddManualStopCompletion : Migration
  {
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.AddColumn<DateTime>(
        name: "ManualCompletedAt",
        table: "DispatchStops",
        type: "timestamp with time zone",
        nullable: true
      );

      migrationBuilder.AddColumn<Guid>(
        name: "ManualCompletedBy",
        table: "DispatchStops",
        type: "uuid",
        nullable: true
      );

      migrationBuilder.AddColumn<string>(
        name: "ManualCompletedByName",
        table: "DispatchStops",
        type: "character varying(200)",
        maxLength: 200,
        nullable: true
      );

      migrationBuilder.AddColumn<DateTime>(
        name: "ManualCompletionRecordedAt",
        table: "DispatchStops",
        type: "timestamp with time zone",
        nullable: true
      );

      migrationBuilder.AddColumn<long>(
        name: "ManualCompletionRevision",
        table: "DispatchStops",
        type: "bigint",
        nullable: false,
        defaultValue: 0L
      );

      migrationBuilder.CreateTable(
        name: "DispatchStopCompletionEvents",
        columns: table => new
        {
          Id = table.Column<Guid>(type: "uuid", nullable: false),
          DispatchId = table.Column<Guid>(type: "uuid", nullable: false),
          StopId = table.Column<Guid>(type: "uuid", nullable: false),
          Revision = table.Column<long>(type: "bigint", nullable: false),
          CompletedAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: true
          ),
          RecordedAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: false
          ),
          ActorId = table.Column<Guid>(type: "uuid", nullable: false),
        },
        constraints: table =>
        {
          table.PrimaryKey("PK_DispatchStopCompletionEvents", x => x.Id);
        }
      );

      migrationBuilder.CreateIndex(
        name: "IX_DispatchStopCompletionEvents_StopId_Revision",
        table: "DispatchStopCompletionEvents",
        columns: new[] { "StopId", "Revision" },
        unique: true
      );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.DropTable(name: "DispatchStopCompletionEvents");

      migrationBuilder.DropColumn(
        name: "ManualCompletedAt",
        table: "DispatchStops"
      );

      migrationBuilder.DropColumn(
        name: "ManualCompletedBy",
        table: "DispatchStops"
      );

      migrationBuilder.DropColumn(
        name: "ManualCompletedByName",
        table: "DispatchStops"
      );

      migrationBuilder.DropColumn(
        name: "ManualCompletionRecordedAt",
        table: "DispatchStops"
      );

      migrationBuilder.DropColumn(
        name: "ManualCompletionRevision",
        table: "DispatchStops"
      );
    }
  }
}
