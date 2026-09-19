using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
  /// <inheritdoc />
  public partial class StoreVerifiedStopAddresses : Migration
  {
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.AddColumn<DateTime>(
        name: "AddressRetryAfter",
        table: "DispatchStops",
        type: "timestamp with time zone",
        nullable: true
      );

      migrationBuilder.AddColumn<DateTime>(
        name: "AddressVerifiedAt",
        table: "DispatchStops",
        type: "timestamp with time zone",
        nullable: true
      );

      migrationBuilder.AddColumn<string>(
        name: "SourceAddressJson",
        table: "DispatchStops",
        type: "text",
        nullable: false,
        defaultValue: ""
      );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.DropColumn(
        name: "AddressRetryAfter",
        table: "DispatchStops"
      );

      migrationBuilder.DropColumn(
        name: "AddressVerifiedAt",
        table: "DispatchStops"
      );

      migrationBuilder.DropColumn(
        name: "SourceAddressJson",
        table: "DispatchStops"
      );
    }
  }
}
