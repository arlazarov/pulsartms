using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
  /// <inheritdoc />
  public partial class AddRouteChoicePreviews : Migration
  {
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.CreateTable(
        name: "DispatchRoutePreviews",
        columns: table => new
        {
          Id = table.Column<Guid>(type: "uuid", nullable: false),
          PreviewId = table.Column<Guid>(type: "uuid", nullable: false),
          ExpiresAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: false
          ),
          DraftJson = table.Column<string>(type: "text", nullable: false),
        },
        constraints: table =>
        {
          table.PrimaryKey("PK_DispatchRoutePreviews", x => x.Id);
          table.ForeignKey(
            name: "FK_DispatchRoutePreviews_Users_Id",
            column: x => x.Id,
            principalTable: "Users",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade
          );
        }
      );

      migrationBuilder.CreateIndex(
        name: "IX_DispatchRoutePreviews_ExpiresAt",
        table: "DispatchRoutePreviews",
        column: "ExpiresAt"
      );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.DropTable(name: "DispatchRoutePreviews");
    }
  }
}
