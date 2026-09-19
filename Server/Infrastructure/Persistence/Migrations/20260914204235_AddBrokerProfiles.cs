using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBrokerProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProfileJson",
                table: "Customers",
                type: "text",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<long>(
                name: "ProfileRevision",
                table: "Customers",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProfileJson",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "ProfileRevision",
                table: "Customers");
        }
    }
}
