using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
  /// <inheritdoc />
  public partial class AddPlanningSettings : Migration
  {
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.CreateTable(
        name: "FleetPlanningSettings",
        columns: table => new
        {
          Id = table.Column<Guid>(type: "uuid", nullable: false),
          SettingsJson = table.Column<string>(
            type: "character varying(4096)",
            maxLength: 4096,
            nullable: false
          ),
          Revision = table.Column<long>(type: "bigint", nullable: false),
          UpdatedAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: false
          ),
        },
        constraints: table =>
        {
          table.PrimaryKey("PK_FleetPlanningSettings", x => x.Id);
        }
      );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.DropTable(name: "FleetPlanningSettings");
    }
  }
}
