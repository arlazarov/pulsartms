using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDriverGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SelectedDriverGroupId",
                table: "Users",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DriverGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DriverGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DriverGroups_Users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DriverGroupMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    DriverId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DriverGroupMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DriverGroupMembers_DriverGroups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "DriverGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DriverGroupMembers_Drivers_DriverId",
                        column: x => x.DriverId,
                        principalTable: "Drivers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_SelectedDriverGroupId",
                table: "Users",
                column: "SelectedDriverGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_DriverGroupMembers_CompanyId",
                table: "DriverGroupMembers",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_DriverGroupMembers_DriverId",
                table: "DriverGroupMembers",
                column: "DriverId");

            migrationBuilder.CreateIndex(
                name: "IX_DriverGroupMembers_GroupId_DriverId",
                table: "DriverGroupMembers",
                columns: new[] { "GroupId", "DriverId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DriverGroups_CompanyId",
                table: "DriverGroups",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_DriverGroups_CompanyId_OwnerUserId_Name",
                table: "DriverGroups",
                columns: new[] { "CompanyId", "OwnerUserId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DriverGroups_OwnerUserId",
                table: "DriverGroups",
                column: "OwnerUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Users_DriverGroups_SelectedDriverGroupId",
                table: "Users",
                column: "SelectedDriverGroupId",
                principalTable: "DriverGroups",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Users_DriverGroups_SelectedDriverGroupId",
                table: "Users");

            migrationBuilder.DropTable(
                name: "DriverGroupMembers");

            migrationBuilder.DropTable(
                name: "DriverGroups");

            migrationBuilder.DropIndex(
                name: "IX_Users_SelectedDriverGroupId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SelectedDriverGroupId",
                table: "Users");
        }
    }
}
