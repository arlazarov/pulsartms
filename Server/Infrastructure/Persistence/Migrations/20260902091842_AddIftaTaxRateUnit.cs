using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
  /// <inheritdoc />
  public partial class AddIftaTaxRateUnit : Migration
  {
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.AddColumn<string>(
        name: "Unit",
        table: "IftaTaxRates",
        type: "character varying(3)",
        maxLength: 3,
        nullable: false,
        defaultValue: ""
      );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.DropColumn(name: "Unit", table: "IftaTaxRates");
    }
  }
}
