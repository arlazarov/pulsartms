using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
  /// <inheritdoc />
  public partial class EnforceUniqueApplicationRoles : Migration
  {
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.Sql(
        """
        DO $$
        BEGIN
            IF EXISTS (
                SELECT 1 FROM "AspNetUserClaims"
                WHERE "ClaimType" = 'amftms:role'
                GROUP BY "UserId" HAVING COUNT(*) > 1
            ) THEN
                RAISE EXCEPTION 'Duplicate application roles require review before applying EnforceUniqueApplicationRoles.';
            END IF;
        END $$;
        """
      );

      migrationBuilder.CreateIndex(
        name: "UX_AspNetUserClaims_ApplicationRole",
        table: "AspNetUserClaims",
        column: "UserId",
        unique: true,
        filter: "\"ClaimType\" = 'amftms:role'"
      );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.DropIndex(
        name: "UX_AspNetUserClaims_ApplicationRole",
        table: "AspNetUserClaims"
      );
    }
  }
}
