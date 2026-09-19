using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
  /// <inheritdoc />
  public partial class AddSynchronizationCheckpoint : Migration
  {
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.CreateTable(
        name: "SynchronizationCheckpoints",
        columns: table => new
        {
          Id = table.Column<Guid>(type: "uuid", nullable: false),
          Owner = table.Column<string>(type: "text", nullable: false),
          LeaseUntil = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: false
          ),
          UpdatedAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: false
          ),
          StateJson = table.Column<string>(type: "text", nullable: false),
        },
        constraints: table =>
        {
          table.PrimaryKey("PK_SynchronizationCheckpoints", x => x.Id);
        }
      );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.DropTable(name: "SynchronizationCheckpoints");
    }
  }
}
