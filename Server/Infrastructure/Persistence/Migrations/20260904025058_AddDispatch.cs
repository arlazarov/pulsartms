using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDispatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Customers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Dispatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LoadNumber = table.Column<int>(type: "integer", nullable: false),
                    OrderNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    OrderDate = table.Column<DateOnly>(type: "date", nullable: true),
                    InvoiceDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ShipDate = table.Column<DateOnly>(type: "date", nullable: true),
                    DeliveryDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    CustomerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DriverId = table.Column<Guid>(type: "uuid", nullable: true),
                    DriverName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CarrierName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TruckId = table.Column<Guid>(type: "uuid", nullable: true),
                    TruckNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TrailerId = table.Column<Guid>(type: "uuid", nullable: true),
                    TrailerNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    LoadedMiles = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    Price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    LastSyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Dispatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Dispatches_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Dispatches_Drivers_DriverId",
                        column: x => x.DriverId,
                        principalTable: "Drivers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Dispatches_Trailers_TrailerId",
                        column: x => x.TrailerId,
                        principalTable: "Trailers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Dispatches_Trucks_TruckId",
                        column: x => x.TruckId,
                        principalTable: "Trucks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "DispatchStops",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DispatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Job = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    City = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Province = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Country = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ZipCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Latitude = table.Column<decimal>(type: "numeric(10,7)", precision: 10, scale: 7, nullable: true),
                    Longitude = table.Column<decimal>(type: "numeric(10,7)", precision: 10, scale: 7, nullable: true),
                    DriverId = table.Column<Guid>(type: "uuid", nullable: true),
                    DriverName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CoDriverId = table.Column<Guid>(type: "uuid", nullable: true),
                    CoDriverName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CarrierName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TruckId = table.Column<Guid>(type: "uuid", nullable: true),
                    TruckNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TrailerId = table.Column<Guid>(type: "uuid", nullable: true),
                    TrailerNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Commodity = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: false),
                    StopNo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Weight = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    WeightUnit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Pieces = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    Pallets = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    Temperature = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TemperatureUnit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ScheduledDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ScheduledTime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    ScheduledDate2 = table.Column<DateOnly>(type: "date", nullable: true),
                    ScheduledTime2 = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    IsWindow = table.Column<bool>(type: "boolean", nullable: false),
                    ArrivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PickedUpAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeliveredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DepartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DispatchStops", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DispatchStops_Dispatches_DispatchId",
                        column: x => x.DispatchId,
                        principalTable: "Dispatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DispatchStops_Drivers_CoDriverId",
                        column: x => x.CoDriverId,
                        principalTable: "Drivers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_DispatchStops_Drivers_DriverId",
                        column: x => x.DriverId,
                        principalTable: "Drivers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_DispatchStops_Trailers_TrailerId",
                        column: x => x.TrailerId,
                        principalTable: "Trailers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_DispatchStops_Trucks_TruckId",
                        column: x => x.TruckId,
                        principalTable: "Trucks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_NormalizedName",
                table: "Customers",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Dispatches_CustomerId",
                table: "Dispatches",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Dispatches_DriverId",
                table: "Dispatches",
                column: "DriverId");

            migrationBuilder.CreateIndex(
                name: "IX_Dispatches_LoadNumber",
                table: "Dispatches",
                column: "LoadNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Dispatches_TrailerId",
                table: "Dispatches",
                column: "TrailerId");

            migrationBuilder.CreateIndex(
                name: "IX_Dispatches_TruckId",
                table: "Dispatches",
                column: "TruckId");

            migrationBuilder.CreateIndex(
                name: "IX_DispatchStops_CoDriverId",
                table: "DispatchStops",
                column: "CoDriverId");

            migrationBuilder.CreateIndex(
                name: "IX_DispatchStops_DispatchId",
                table: "DispatchStops",
                column: "DispatchId");

            migrationBuilder.CreateIndex(
                name: "IX_DispatchStops_DispatchId_Sequence",
                table: "DispatchStops",
                columns: new[] { "DispatchId", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_DispatchStops_DriverId",
                table: "DispatchStops",
                column: "DriverId");

            migrationBuilder.CreateIndex(
                name: "IX_DispatchStops_TrailerId",
                table: "DispatchStops",
                column: "TrailerId");

            migrationBuilder.CreateIndex(
                name: "IX_DispatchStops_TruckId",
                table: "DispatchStops",
                column: "TruckId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DispatchStops");

            migrationBuilder.DropTable(
                name: "Dispatches");

            migrationBuilder.DropTable(
                name: "Customers");
        }
    }
}
