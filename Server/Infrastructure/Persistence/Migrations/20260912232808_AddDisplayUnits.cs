using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
  /// <inheritdoc />
  public partial class AddDisplayUnits : Migration
  {
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.AddColumn<string>(
        name: "DistanceUnit",
        table: "DispatchSettings",
        type: "character varying(16)",
        maxLength: 16,
        nullable: false,
        defaultValue: "both"
      );

      migrationBuilder.AddColumn<string>(
        name: "TemperatureUnit",
        table: "DispatchSettings",
        type: "character varying(16)",
        maxLength: 16,
        nullable: false,
        defaultValue: "both"
      );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.DropColumn(
        name: "DistanceUnit",
        table: "DispatchSettings"
      );

      migrationBuilder.DropColumn(
        name: "TemperatureUnit",
        table: "DispatchSettings"
      );
    }
  }
}
