using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
  /// <inheritdoc />
  public partial class AddStopCorrections : Migration
  {
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.AddColumn<string>(
        name: "BeforeJson",
        table: "DispatchWorkspaceRevisions",
        type: "text",
        nullable: false,
        defaultValue: ""
      );

      migrationBuilder.AddColumn<bool>(
        name: "CompletionOverride",
        table: "DispatchStops",
        type: "boolean",
        nullable: true
      );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.DropColumn(
        name: "BeforeJson",
        table: "DispatchWorkspaceRevisions"
      );

      migrationBuilder.DropColumn(
        name: "CompletionOverride",
        table: "DispatchStops"
      );
    }
  }
}
