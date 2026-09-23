using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFileStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ManagedFileBlobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Content = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManagedFileBlobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StorageConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    Root = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    RootName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ProtectedSecret = table.Column<string>(type: "character varying(16384)", maxLength: 16384, nullable: true),
                    PendingUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorageConnections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StorageLayouts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    LoadsFolder = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    LoadTemplate = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CancelledSuffix = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    InboxFolder = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorageLayouts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StoredFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ObjectKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Folder = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    OriginalName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    UploadToken = table.Column<Guid>(type: "uuid", nullable: true),
                    UploadLeaseUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoredFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoredFiles_StorageConnections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalTable: "StorageConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ManagedFileBlobs_CompanyId",
                table: "ManagedFileBlobs",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_StorageConnections_Default",
                table: "StorageConnections",
                column: "CompanyId",
                unique: true,
                filter: "\"IsDefault\"");

            migrationBuilder.CreateIndex(
                name: "IX_StorageLayouts_CompanyId",
                table: "StorageLayouts",
                column: "CompanyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoredFiles_CompanyId",
                table: "StoredFiles",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_StoredFiles_ConnectionId_Folder",
                table: "StoredFiles",
                columns: new[] { "ConnectionId", "Folder" });

            migrationBuilder.CreateIndex(
                name: "IX_StoredFiles_ConnectionId_ObjectKey",
                table: "StoredFiles",
                columns: new[] { "ConnectionId", "ObjectKey" },
                unique: true,
                filter: "\"ObjectKey\" <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_StoredFiles_State_UpdatedAt",
                table: "StoredFiles",
                columns: new[] { "State", "UpdatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ManagedFileBlobs");

            migrationBuilder.DropTable(
                name: "StorageLayouts");

            migrationBuilder.DropTable(
                name: "StoredFiles");

            migrationBuilder.DropTable(
                name: "StorageConnections");
        }
    }
}
