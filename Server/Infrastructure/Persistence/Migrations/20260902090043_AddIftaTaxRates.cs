using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
  /// <inheritdoc />
  public partial class AddIftaTaxRates : Migration
  {
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.CreateTable(
        name: "IftaTaxRates",
        columns: table => new
        {
          Id = table.Column<Guid>(type: "uuid", nullable: false),
          Jurisdiction = table.Column<string>(
            type: "character varying(2)",
            maxLength: 2,
            nullable: false
          ),
          FuelType = table.Column<string>(
            type: "character varying(50)",
            maxLength: 50,
            nullable: false
          ),
          Rate = table.Column<decimal>(
            type: "numeric(10,4)",
            precision: 10,
            scale: 4,
            nullable: false
          ),
          Currency = table.Column<string>(
            type: "character varying(3)",
            maxLength: 3,
            nullable: false
          ),
          EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
          EffectiveTo = table.Column<DateOnly>(type: "date", nullable: false),
        },
        constraints: table =>
        {
          table.PrimaryKey("PK_IftaTaxRates", x => x.Id);
        }
      );

      migrationBuilder.CreateIndex(
        name: "IX_IftaTaxRates_Jurisdiction_FuelType_EffectiveFrom",
        table: "IftaTaxRates",
        columns: new[] { "Jurisdiction", "FuelType", "EffectiveFrom" },
        unique: true
      );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.DropTable(name: "IftaTaxRates");
    }
  }
}
