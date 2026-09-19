using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
  /// <inheritdoc />
  public partial class PersistRoutingFailures : Migration
  {
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.AddColumn<string>(
        name: "ErrorMessage",
        table: "RoutingApiCalls",
        type: "text",
        nullable: true
      );

      migrationBuilder.AddColumn<string>(
        name: "ErrorMessage",
        table: "DispatchDeadheads",
        type: "text",
        nullable: true
      );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.DropColumn(
        name: "ErrorMessage",
        table: "RoutingApiCalls"
      );

      migrationBuilder.DropColumn(
        name: "ErrorMessage",
        table: "DispatchDeadheads"
      );
    }
  }
}
