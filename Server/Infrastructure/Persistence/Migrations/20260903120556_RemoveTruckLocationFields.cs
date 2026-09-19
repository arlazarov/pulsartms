using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
  /// <inheritdoc />
  public partial class RemoveTruckLocationFields : Migration
  {
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.DropColumn(name: "Heading", table: "Trucks");

      migrationBuilder.DropColumn(name: "Latitude", table: "Trucks");

      migrationBuilder.DropColumn(name: "LocationSavedAt", table: "Trucks");

      migrationBuilder.DropColumn(name: "LocationUpdatedAt", table: "Trucks");

      migrationBuilder.DropColumn(name: "Longitude", table: "Trucks");

      migrationBuilder.DropColumn(name: "Speed", table: "Trucks");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.AddColumn<decimal>(
        name: "Heading",
        table: "Trucks",
        type: "numeric(6,2)",
        precision: 6,
        scale: 2,
        nullable: true
      );

      migrationBuilder.AddColumn<decimal>(
        name: "Latitude",
        table: "Trucks",
        type: "numeric(9,6)",
        precision: 9,
        scale: 6,
        nullable: true
      );

      migrationBuilder.AddColumn<DateTime>(
        name: "LocationSavedAt",
        table: "Trucks",
        type: "timestamp with time zone",
        nullable: true
      );

      migrationBuilder.AddColumn<DateTime>(
        name: "LocationUpdatedAt",
        table: "Trucks",
        type: "timestamp with time zone",
        nullable: true
      );

      migrationBuilder.AddColumn<decimal>(
        name: "Longitude",
        table: "Trucks",
        type: "numeric(9,6)",
        precision: 9,
        scale: 6,
        nullable: true
      );

      migrationBuilder.AddColumn<decimal>(
        name: "Speed",
        table: "Trucks",
        type: "numeric(8,3)",
        precision: 8,
        scale: 3,
        nullable: true
      );
    }
  }
}
