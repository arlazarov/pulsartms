using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
  /// <inheritdoc />
  public partial class AddTruckAssignmentStart : Migration
  {
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.AddColumn<DateTime>(
        name: "PlanningAssignmentRecordedAt",
        table: "Dispatches",
        type: "timestamp with time zone",
        nullable: true
      );

      migrationBuilder.AddColumn<Guid>(
        name: "PlanningAssignmentRecordedBy",
        table: "Dispatches",
        type: "uuid",
        nullable: true
      );

      migrationBuilder.AddColumn<long>(
        name: "PlanningAssignmentRevision",
        table: "Dispatches",
        type: "bigint",
        nullable: false,
        defaultValue: 0L
      );

      migrationBuilder.AddColumn<Guid>(
        name: "PlanningFromStopId",
        table: "Dispatches",
        type: "uuid",
        nullable: true
      );

      migrationBuilder.AddColumn<Guid>(
        name: "PlanningTruckId",
        table: "Dispatches",
        type: "uuid",
        nullable: true
      );

      migrationBuilder.CreateIndex(
        name: "IX_Dispatches_PlanningTruckId",
        table: "Dispatches",
        column: "PlanningTruckId"
      );

      migrationBuilder.AddForeignKey(
        name: "FK_Dispatches_Trucks_PlanningTruckId",
        table: "Dispatches",
        column: "PlanningTruckId",
        principalTable: "Trucks",
        principalColumn: "Id",
        onDelete: ReferentialAction.Restrict
      );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.DropForeignKey(
        name: "FK_Dispatches_Trucks_PlanningTruckId",
        table: "Dispatches"
      );

      migrationBuilder.DropIndex(
        name: "IX_Dispatches_PlanningTruckId",
        table: "Dispatches"
      );

      migrationBuilder.DropColumn(
        name: "PlanningAssignmentRecordedAt",
        table: "Dispatches"
      );

      migrationBuilder.DropColumn(
        name: "PlanningAssignmentRecordedBy",
        table: "Dispatches"
      );

      migrationBuilder.DropColumn(
        name: "PlanningAssignmentRevision",
        table: "Dispatches"
      );

      migrationBuilder.DropColumn(
        name: "PlanningFromStopId",
        table: "Dispatches"
      );

      migrationBuilder.DropColumn(name: "PlanningTruckId", table: "Dispatches");
    }
  }
}
