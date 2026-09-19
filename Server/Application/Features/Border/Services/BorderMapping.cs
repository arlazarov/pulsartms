using System.Text.Json;
using Application.Features.Border.Interfaces;
using Application.Features.Border.Models;
using CrewEntity = Domain.Entities.Border.BorderCrew;
using Entity = Domain.Entities.Border.BorderCrossing;
using EquipmentEntity = Domain.Entities.Border.BorderEquipment;
using PartyEntity = Domain.Entities.Shipments.ShipmentParty;
using Shipment = Application.Features.Shipments.Models.Shipment;
using ShipmentEntity = Domain.Entities.Border.BorderShipment;
using ShipmentParty = Application.Features.Shipments.Models.ShipmentParty;

namespace Application.Features.Border.Services;

public static class BorderMapping
{
  public static BorderCrossing Project(
    Entity row,
    IBorderDataProtection protection
  )
  {
    var result = Copy<BorderCrossing>(row);
    foreach (var member in result.Crew)
      member.Details = JsonSerializer.Deserialize<BorderPerson>(
        protection.Unprotect(
          row.Id,
          row.Crew.Single(x => x.Id == member.Id).ProtectedDetails
        )
      )!;
    foreach (var shipment in result.Shipments)
      shipment.Snapshot = JsonSerializer.Deserialize<Shipment>(
        row.Shipments.Single(x => x.Id == shipment.Id).ShipmentSnapshotJson
      );
    return result;
  }

  public static void Apply(
    Entity target,
    BorderCrossing source,
    IBorderDataProtection protection
  )
  {
    target.Reference = source.Reference;
    target.DestinationCountry = source.DestinationCountry;
    target.PortOfEntry = source.PortOfEntry;
    target.ArrivalDate = source.ArrivalDate;
    target.ArrivalTime = source.ArrivalTime;
    target.ArrivalTimeZone = source.ArrivalTimeZone;
    target.CarrierName = source.CarrierName;
    target.Scac = source.Scac;
    target.CanadianCarrierCode = source.CanadianCarrierCode;
    target.EmptyConveyance = source.EmptyConveyance;
    target.SourceLegId = source.SourceLegId;
    target.SourceStopId = source.SourceStopId;
    target.SourceRevision = source.SourceRevision;
    target.CarrierAddress = Copy<PartyEntity>(source.CarrierAddress);
    target.Equipment.RemoveAll(x => !source.Equipment.Any(y => y.Id == x.Id));
    foreach (var input in source.Equipment)
    {
      var row = target.Equipment.SingleOrDefault(x => x.Id == input.Id);
      if (row is null)
      {
        row = new EquipmentEntity { Id = input.Id };
        target.Equipment.Add(row);
      }
      row.TruckId = input.TruckId;
      row.TrailerId = input.TrailerId;
      row.Kind = input.Kind;
      row.UnitNumber = input.UnitNumber;
      row.Vin = input.Vin;
      row.EquipmentType = input.EquipmentType;
      row.PlateNumber = input.PlateNumber;
      row.PlateRegion = input.PlateRegion;
      row.PlateCountry = input.PlateCountry;
      row.ContainerNumber = input.ContainerNumber;
      row.SealNumbers = input.SealNumbers;
    }
    target.Crew.RemoveAll(x => !source.Crew.Any(y => y.Id == x.Id));
    foreach (var input in source.Crew)
    {
      var row = target.Crew.SingleOrDefault(x => x.Id == input.Id);
      if (row is null)
      {
        row = new CrewEntity { Id = input.Id };
        target.Crew.Add(row);
      }
      row.DriverId = input.DriverId;
      row.Role = input.Role;
      row.DisplayName = input.DisplayName;
      row.ProtectedDetails = protection.Protect(
        source.Id,
        JsonSerializer.Serialize(input.Details)
      );
    }
    target.Shipments.RemoveAll(x => !source.Shipments.Any(y => y.Id == x.Id));
    foreach (var input in source.Shipments)
    {
      var row = target.Shipments.SingleOrDefault(x => x.Id == input.Id);
      if (row is null)
      {
        row = new ShipmentEntity { Id = input.Id };
        target.Shipments.Add(row);
      }
      row.ShipmentId = input.ShipmentId;
      row.ShipmentRevision = input.ShipmentRevision;
      row.Procedure = input.Procedure;
      row.ParsNumber = input.ParsNumber;
      row.PapsNumber = input.PapsNumber;
      row.ReleaseOffice = input.ReleaseOffice;
      row.LoadingCity = input.LoadingCity;
      row.LoadingRegion = input.LoadingRegion;
      row.LoadingCountry = input.LoadingCountry;
      row.Consolidated = input.Consolidated;
      row.EquipmentId = input.EquipmentId;
      row.Importer = Copy<PartyEntity>(input.Importer);
      row.CustomsBroker = Copy<PartyEntity>(input.CustomsBroker);
      row.ShipmentSnapshotJson = JsonSerializer.Serialize(input.Snapshot);
    }
  }

  private static T Copy<T>(object value) =>
    JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
}
