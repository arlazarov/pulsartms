using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IsolateCarrierIntegrationCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_IntegrationCredentialSettings",
                table: "IntegrationCredentialSettings");

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "IntegrationCredentialSettings",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("a0f0a0f0-0000-4000-8000-000000000001"));

            migrationBuilder.AddPrimaryKey(
                name: "PK_IntegrationCredentialSettings",
                table: "IntegrationCredentialSettings",
                columns: new[] { "CompanyId", "Provider" });

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationCredentialSettings_CompanyId",
                table: "IntegrationCredentialSettings",
                column: "CompanyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException(
                "Carrier credential isolation requires explicit rollback recovery; "
                + "company-owned credentials cannot be merged automatically.");
        }
    }
}
