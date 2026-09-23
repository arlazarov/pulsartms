using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFiledDriverFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SourceAttachmentId",
                table: "DispatchDocuments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StoredFileId",
                table: "DispatchDocuments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DispatchDocuments_DispatchId_SourceAttachmentId",
                table: "DispatchDocuments",
                columns: new[] { "DispatchId", "SourceAttachmentId" },
                unique: true,
                filter: "\"SourceAttachmentId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DispatchDocuments_StoredFileId",
                table: "DispatchDocuments",
                column: "StoredFileId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DispatchDocuments_DispatchId_SourceAttachmentId",
                table: "DispatchDocuments");

            migrationBuilder.DropIndex(
                name: "IX_DispatchDocuments_StoredFileId",
                table: "DispatchDocuments");

            migrationBuilder.DropColumn(
                name: "SourceAttachmentId",
                table: "DispatchDocuments");

            migrationBuilder.DropColumn(
                name: "StoredFileId",
                table: "DispatchDocuments");
        }
    }
}
