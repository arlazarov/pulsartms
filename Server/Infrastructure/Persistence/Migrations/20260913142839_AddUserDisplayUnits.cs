using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
  /// <inheritdoc />
  public partial class AddUserDisplayUnits : Migration
  {
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.AddColumn<string>(
        name: "DistanceUnit",
        table: "Users",
        type: "character varying(16)",
        maxLength: 16,
        nullable: false,
        defaultValue: "both"
      );

      migrationBuilder.AddColumn<string>(
        name: "TemperatureUnit",
        table: "Users",
        type: "character varying(16)",
        maxLength: 16,
        nullable: false,
        defaultValue: "both"
      );

      migrationBuilder.Sql(
        """
        UPDATE "Users"
        SET "TemperatureUnit" = s."TemperatureUnit",
            "DistanceUnit" = s."DistanceUnit"
        FROM "DispatchSettings" AS s
        WHERE s."Id" = '731f4d30-c85a-4af0-a14a-29db18bd4a47';
        """
      );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.DropColumn(name: "DistanceUnit", table: "Users");

      migrationBuilder.DropColumn(name: "TemperatureUnit", table: "Users");
    }
  }
}
