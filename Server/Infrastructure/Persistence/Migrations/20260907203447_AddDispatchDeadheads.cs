using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
  /// <inheritdoc />
  public partial class AddDispatchDeadheads : Migration
  {
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.CreateTable(
        name: "DispatchDeadheads",
        columns: table => new
        {
          Id = table.Column<Guid>(type: "uuid", nullable: false),
          DispatchId = table.Column<Guid>(type: "uuid", nullable: false),
          PreviousDispatchId = table.Column<Guid>(
            type: "uuid",
            nullable: false
          ),
          InputHash = table.Column<string>(
            type: "character varying(64)",
            maxLength: 64,
            nullable: false
          ),
          Miles = table.Column<decimal>(
            type: "numeric(18,3)",
            precision: 18,
            scale: 3,
            nullable: true
          ),
          CalculatedAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: true
          ),
          RetryAfter = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: false
          ),
        },
        constraints: table =>
        {
          table.PrimaryKey("PK_DispatchDeadheads", x => x.Id);
          table.ForeignKey(
            name: "FK_DispatchDeadheads_Dispatches_DispatchId",
            column: x => x.DispatchId,
            principalTable: "Dispatches",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade
          );
        }
      );

      migrationBuilder.CreateIndex(
        name: "IX_DispatchDeadheads_DispatchId",
        table: "DispatchDeadheads",
        column: "DispatchId",
        unique: true
      );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.DropTable(name: "DispatchDeadheads");
    }
  }
}
