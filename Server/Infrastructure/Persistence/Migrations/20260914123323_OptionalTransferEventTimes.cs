using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
  /// <inheritdoc />
  public partial class OptionalTransferEventTimes : Migration
  {
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.DropIndex(
        name: "IX_TrailerCustodyIntervals_TrailerId",
        table: "TrailerCustodyIntervals"
      );

      migrationBuilder.AlterColumn<DateTime>(
        name: "ReleasedAt",
        table: "TrailerCustodyIntervals",
        type: "timestamp with time zone",
        nullable: true,
        oldClrType: typeof(DateTime),
        oldType: "timestamp with time zone"
      );

      migrationBuilder.CreateIndex(
        name: "IX_TrailerCustodyIntervals_TrailerId",
        table: "TrailerCustodyIntervals",
        column: "TrailerId",
        unique: true,
        filter: "\"ReceivedBy\" IS NULL"
      );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.DropIndex(
        name: "IX_TrailerCustodyIntervals_TrailerId",
        table: "TrailerCustodyIntervals"
      );

      migrationBuilder.AlterColumn<DateTime>(
        name: "ReleasedAt",
        table: "TrailerCustodyIntervals",
        type: "timestamp with time zone",
        nullable: false,
        oldClrType: typeof(DateTime),
        oldType: "timestamp with time zone",
        oldNullable: true
      );

      migrationBuilder.CreateIndex(
        name: "IX_TrailerCustodyIntervals_TrailerId",
        table: "TrailerCustodyIntervals",
        column: "TrailerId",
        unique: true,
        filter: "\"ReceivedAt\" IS NULL"
      );
    }
  }
}
