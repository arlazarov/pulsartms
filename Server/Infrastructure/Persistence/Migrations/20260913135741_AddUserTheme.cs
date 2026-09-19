using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
  /// <inheritdoc />
  public partial class AddUserTheme : Migration
  {
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.AddColumn<string>(
        name: "Theme",
        table: "Users",
        type: "character varying(5)",
        maxLength: 5,
        nullable: false,
        defaultValue: "light"
      );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.DropColumn(name: "Theme", table: "Users");
    }
  }
}
