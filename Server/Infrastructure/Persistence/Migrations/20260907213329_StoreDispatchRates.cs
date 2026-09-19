using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
  /// <inheritdoc />
  public partial class StoreDispatchRates : Migration
  {
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.CreateTable(
        name: "DispatchRates",
        columns: table => new
        {
          Id = table.Column<Guid>(type: "uuid", nullable: false),
          DispatchId = table.Column<Guid>(type: "uuid", nullable: false),
          Price = table.Column<decimal>(
            type: "numeric(18,2)",
            precision: 18,
            scale: 2,
            nullable: true
          ),
          Currency = table.Column<string>(
            type: "character varying(10)",
            maxLength: 10,
            nullable: false
          ),
          LoadedMiles = table.Column<decimal>(
            type: "numeric(12,2)",
            precision: 12,
            scale: 2,
            nullable: true
          ),
          EmptyMiles = table.Column<decimal>(
            type: "numeric(18,3)",
            precision: 18,
            scale: 3,
            nullable: true
          ),
          ConnectionHash = table.Column<string>(
            type: "character varying(64)",
            maxLength: 64,
            nullable: false
          ),
          LoadedRatePerMile = table.Column<decimal>(
            type: "numeric(18,6)",
            precision: 18,
            scale: 6,
            nullable: true
          ),
          TotalRatePerMile = table.Column<decimal>(
            type: "numeric(18,6)",
            precision: 18,
            scale: 6,
            nullable: true
          ),
          CalculatedAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: false
          ),
        },
        constraints: table =>
        {
          table.PrimaryKey("PK_DispatchRates", x => x.Id);
          table.ForeignKey(
            name: "FK_DispatchRates_Dispatches_DispatchId",
            column: x => x.DispatchId,
            principalTable: "Dispatches",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade
          );
        }
      );

      migrationBuilder.CreateIndex(
        name: "IX_DispatchRates_DispatchId",
        table: "DispatchRates",
        column: "DispatchId",
        unique: true
      );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.DropTable(name: "DispatchRates");
    }
  }
}
