using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
  /// <inheritdoc />
  public partial class AddDispatchRouteChoices : Migration
  {
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.AddColumn<long>(
        name: "RouteChoiceRevision",
        table: "Dispatches",
        type: "bigint",
        nullable: false,
        defaultValue: 0L
      );

      migrationBuilder.CreateTable(
        name: "DispatchRouteChoices",
        columns: table => new
        {
          Id = table.Column<Guid>(type: "uuid", nullable: false),
          DispatchId = table.Column<Guid>(type: "uuid", nullable: false),
          InputHash = table.Column<string>(
            type: "character varying(64)",
            maxLength: 64,
            nullable: false
          ),
          ChoiceJson = table.Column<string>(type: "text", nullable: false),
          Revision = table.Column<long>(type: "bigint", nullable: false),
          RecordedAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: false
          ),
          RecordedBy = table.Column<Guid>(type: "uuid", nullable: false),
        },
        constraints: table =>
        {
          table.PrimaryKey("PK_DispatchRouteChoices", x => x.Id);
          table.ForeignKey(
            name: "FK_DispatchRouteChoices_Dispatches_DispatchId",
            column: x => x.DispatchId,
            principalTable: "Dispatches",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade
          );
        }
      );

      migrationBuilder.CreateIndex(
        name: "IX_DispatchRouteChoices_DispatchId",
        table: "DispatchRouteChoices",
        column: "DispatchId",
        unique: true
      );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.DropTable(name: "DispatchRouteChoices");

      migrationBuilder.DropColumn(
        name: "RouteChoiceRevision",
        table: "Dispatches"
      );
    }
  }
}
