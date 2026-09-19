using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
  /// <inheritdoc />
  public partial class AddStopOperations : Migration
  {
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.AddColumn<string>(
        name: "ManualAction",
        table: "DispatchStops",
        type: "character varying(50)",
        maxLength: 50,
        nullable: true
      );

      migrationBuilder.AddColumn<string>(
        name: "ManualStateAfter",
        table: "DispatchStops",
        type: "character varying(20)",
        maxLength: 20,
        nullable: true
      );

      migrationBuilder.AddColumn<DateTime>(
        name: "OperationRecordedAt",
        table: "DispatchStops",
        type: "timestamp with time zone",
        nullable: true
      );

      migrationBuilder.AddColumn<Guid>(
        name: "OperationRecordedBy",
        table: "DispatchStops",
        type: "uuid",
        nullable: true
      );

      migrationBuilder.AddColumn<long>(
        name: "OperationRevision",
        table: "DispatchStops",
        type: "bigint",
        nullable: false,
        defaultValue: 0L
      );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.DropColumn(name: "ManualAction", table: "DispatchStops");

      migrationBuilder.DropColumn(
        name: "ManualStateAfter",
        table: "DispatchStops"
      );

      migrationBuilder.DropColumn(
        name: "OperationRecordedAt",
        table: "DispatchStops"
      );

      migrationBuilder.DropColumn(
        name: "OperationRecordedBy",
        table: "DispatchStops"
      );

      migrationBuilder.DropColumn(
        name: "OperationRevision",
        table: "DispatchStops"
      );
    }
  }
}
