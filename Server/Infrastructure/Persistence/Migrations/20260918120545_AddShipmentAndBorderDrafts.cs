using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddShipmentAndBorderDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BorderCrossings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Reference = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    DestinationCountry = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    PortOfEntry = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ArrivalDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ArrivalTime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    ArrivalTimeZone = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    CarrierName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Scac = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    CanadianCarrierCode = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    EmptyConveyance = table.Column<bool>(type: "boolean", nullable: false),
                    SourceLegId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceStopId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceRevision = table.Column<long>(type: "bigint", nullable: true),
                    CarrierAddress_Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    CarrierAddress_AddressLine1 = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    CarrierAddress_AddressLine2 = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    CarrierAddress_City = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    CarrierAddress_Region = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    CarrierAddress_Country = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    CarrierAddress_PostalCode = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    CarrierAddress_ContactName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    CarrierAddress_Phone = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    CarrierAddress_Email = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BorderCrossings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BorderSaveReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    CrossingId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProtectedResponse = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BorderSaveReceipts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Shipments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LoadId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    BillOfLading = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PickupStopId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeliveryStopId = table.Column<Guid>(type: "uuid", nullable: true),
                    Shipper_Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Shipper_AddressLine1 = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Shipper_AddressLine2 = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Shipper_City = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Shipper_Region = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Shipper_Country = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Shipper_PostalCode = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Shipper_ContactName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Shipper_Phone = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Shipper_Email = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Consignee_Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Consignee_AddressLine1 = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Consignee_AddressLine2 = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Consignee_City = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Consignee_Region = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Consignee_Country = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Consignee_PostalCode = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Consignee_ContactName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Consignee_Phone = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Consignee_Email = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Shipments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Shipments_Dispatches_LoadId",
                        column: x => x.LoadId,
                        principalTable: "Dispatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ShipmentSaveReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    AggregateId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ResponseJson = table.Column<string>(type: "text", nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShipmentSaveReceipts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BorderCrew",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CrossingId = table.Column<Guid>(type: "uuid", nullable: false),
                    DriverId = table.Column<Guid>(type: "uuid", nullable: true),
                    Role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ProtectedDetails = table.Column<string>(type: "character varying(64000)", maxLength: 64000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BorderCrew", x => new { x.CrossingId, x.Id });
                    table.ForeignKey(
                        name: "FK_BorderCrew_BorderCrossings_CrossingId",
                        column: x => x.CrossingId,
                        principalTable: "BorderCrossings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BorderCrew_Drivers_DriverId",
                        column: x => x.DriverId,
                        principalTable: "Drivers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BorderEquipment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CrossingId = table.Column<Guid>(type: "uuid", nullable: false),
                    TruckId = table.Column<Guid>(type: "uuid", nullable: true),
                    TrailerId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    UnitNumber = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Vin = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    EquipmentType = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    PlateNumber = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    PlateRegion = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    PlateCountry = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ContainerNumber = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    SealNumbers = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BorderEquipment", x => new { x.CrossingId, x.Id });
                    table.ForeignKey(
                        name: "FK_BorderEquipment_BorderCrossings_CrossingId",
                        column: x => x.CrossingId,
                        principalTable: "BorderCrossings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BorderEquipment_Trailers_TrailerId",
                        column: x => x.TrailerId,
                        principalTable: "Trailers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BorderEquipment_Trucks_TruckId",
                        column: x => x.TruckId,
                        principalTable: "Trucks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BorderShipments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CrossingId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShipmentRevision = table.Column<long>(type: "bigint", nullable: false),
                    Procedure = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ParsNumber = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    PapsNumber = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ReleaseOffice = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    LoadingCity = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    LoadingRegion = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    LoadingCountry = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Consolidated = table.Column<bool>(type: "boolean", nullable: false),
                    EquipmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Importer_Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Importer_AddressLine1 = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Importer_AddressLine2 = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Importer_City = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Importer_Region = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Importer_Country = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Importer_PostalCode = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Importer_ContactName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Importer_Phone = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Importer_Email = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    CustomsBroker_Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    CustomsBroker_AddressLine1 = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    CustomsBroker_AddressLine2 = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    CustomsBroker_City = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    CustomsBroker_Region = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    CustomsBroker_Country = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    CustomsBroker_PostalCode = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    CustomsBroker_ContactName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    CustomsBroker_Phone = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    CustomsBroker_Email = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ShipmentSnapshotJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BorderShipments", x => new { x.CrossingId, x.Id });
                    table.ForeignKey(
                        name: "FK_BorderShipments_BorderCrossings_CrossingId",
                        column: x => x.CrossingId,
                        principalTable: "BorderCrossings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BorderShipments_Shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalTable: "Shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomsCommodities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    PackageType = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    WeightUnit = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Marks = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Classification = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    OriginCountry = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: true),
                    Weight = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomsCommodities", x => new { x.ShipmentId, x.Id });
                    table.ForeignKey(
                        name: "FK_CustomsCommodities_Shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalTable: "Shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BorderCrew_DriverId",
                table: "BorderCrew",
                column: "DriverId");

            migrationBuilder.CreateIndex(
                name: "IX_BorderEquipment_TrailerId",
                table: "BorderEquipment",
                column: "TrailerId");

            migrationBuilder.CreateIndex(
                name: "IX_BorderEquipment_TruckId",
                table: "BorderEquipment",
                column: "TruckId");

            migrationBuilder.CreateIndex(
                name: "IX_BorderSaveReceipts_CrossingId",
                table: "BorderSaveReceipts",
                column: "CrossingId");

            migrationBuilder.CreateIndex(
                name: "IX_BorderShipments_CrossingId_ShipmentId",
                table: "BorderShipments",
                columns: new[] { "CrossingId", "ShipmentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BorderShipments_ShipmentId",
                table: "BorderShipments",
                column: "ShipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_LoadId",
                table: "Shipments",
                column: "LoadId");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentSaveReceipts_AggregateId",
                table: "ShipmentSaveReceipts",
                column: "AggregateId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BorderCrew");

            migrationBuilder.DropTable(
                name: "BorderEquipment");

            migrationBuilder.DropTable(
                name: "BorderSaveReceipts");

            migrationBuilder.DropTable(
                name: "BorderShipments");

            migrationBuilder.DropTable(
                name: "CustomsCommodities");

            migrationBuilder.DropTable(
                name: "ShipmentSaveReceipts");

            migrationBuilder.DropTable(
                name: "BorderCrossings");

            migrationBuilder.DropTable(
                name: "Shipments");
        }
    }
}
