using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
  /// <inheritdoc />
  public partial class AddIntegrationCredentialSettings : Migration
  {
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.CreateTable(
        name: "IntegrationCredentialSettings",
        columns: table => new
        {
          Provider = table.Column<string>(
            type: "character varying(32)",
            maxLength: 32,
            nullable: false
          ),
          ProtectedValues = table.Column<string>(
            type: "character varying(65536)",
            maxLength: 65536,
            nullable: true
          ),
          Revision = table.Column<long>(type: "bigint", nullable: false),
          UpdatedAt = table.Column<DateTime>(
            type: "timestamp with time zone",
            nullable: false
          ),
        },
        constraints: table =>
        {
          table.PrimaryKey("PK_IntegrationCredentialSettings", x => x.Provider);
        }
      );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.DropTable(name: "IntegrationCredentialSettings");
    }
  }
}
