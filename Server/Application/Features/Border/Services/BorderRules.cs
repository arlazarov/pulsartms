using Application.Features.Border.Models;
using Application.Features.Shipments.Models;
using Application.Features.Shipments.Services;

namespace Application.Features.Border.Services;

public static class BorderRules
{
  public static string? Validate(BorderCrossing? draft)
  {
    if (
      draft is null
      || draft.Id == Guid.Empty
      || !Text(draft)
      || draft.CarrierAddress is null
      || !Text(draft.CarrierAddress)
      || draft.Shipments is null
      || draft.Crew is null
      || draft.Equipment is null
    )
      return "Provide crossing fields within their supported limits.";
    if (
      draft.DestinationCountry is not ("" or "CA" or "US")
      || draft.Shipments.Count > 25
      || draft.Crew.Count > 6
      || draft.Equipment.Count > 8
    )
      return "Review the destination or number of shipments, people and vehicles.";
    if (
      draft.PortOfEntry.Length > 0
      && !BorderPorts.Contains(draft.DestinationCountry, draft.PortOfEntry)
    )
      return "Choose a port from the selected country's catalog.";
    if (
      !Ids(draft.Shipments.Select(x => x?.Id ?? Guid.Empty))
      || !Ids(draft.Crew.Select(x => x?.Id ?? Guid.Empty))
      || !Ids(draft.Equipment.Select(x => x?.Id ?? Guid.Empty))
    )
      return "Every draft row needs its own identity.";
    if (
      draft.Shipments.Select(x => x.ShipmentId).Distinct().Count()
      != draft.Shipments.Count
    )
      return "Select each shipment only once.";
    foreach (var row in draft.Shipments)
    {
      if (
        row.Snapshot is not null
        && ShipmentRules.Validate(row.Snapshot) is not null
      )
        return "The supplied shipment preview exceeds its supported limits.";

      if (
        !Text(row)
        || row.ShipmentId == Guid.Empty
        || row.ShipmentRevision <= 0
        || row.Procedure is not ("" or "PARS" or "PAPS")
        || row.Importer is null
        || row.CustomsBroker is null
        || !Text(row.Importer)
        || !Text(row.CustomsBroker)
        || row.EquipmentId.HasValue
          && !draft.Equipment.Any(x => x.Id == row.EquipmentId)
      )
        return "Review shipment references, parties and loaded equipment.";
    }
    foreach (var row in draft.Crew)
    {
      if (
        !Text(row)
        || row.Role is not ("driver" or "co-driver" or "passenger")
        || row.Details is null
        || !Text(row.Details)
        || row.Details.Documents is null
        || row.Details.Documents.Count > 10
        || !Ids(row.Details.Documents.Select(x => x?.Id ?? Guid.Empty))
        || row.Details.Documents.Any(x => !Text(x))
      )
        return "Review crew roles and travel document fields.";
      if (row.Role == "passenger" && row.DriverId.HasValue)
        return "Passengers are separate from fleet drivers.";
    }
    foreach (var row in draft.Equipment)
      if (
        !Text(row)
        || row.Kind is not ("truck" or "trailer" or "container")
        || row.Kind == "truck" && row.TrailerId.HasValue
        || row.Kind == "trailer" && row.TruckId.HasValue
        || row.Kind == "container"
          && (row.TruckId.HasValue || row.TrailerId.HasValue)
      )
        return "Select matching fleet resources for each equipment type.";
    if (
      draft.Crew.Count(x => x.Role == "driver") > 1
      || draft.Crew.Count(x => x.Role == "co-driver") > 1
      || draft
        .Crew.Where(x => x.DriverId.HasValue)
        .Select(x => x.DriverId)
        .Distinct()
        .Count() != draft.Crew.Count(x => x.DriverId.HasValue)
      || draft.Equipment.Count(x => x.Kind == "truck") > 1
    )
      return "Use one truck and distinct driver and co-driver assignments.";
    if (
      draft.SourceLegId.HasValue != draft.SourceStopId.HasValue
      || draft.SourceLegId.HasValue != draft.SourceRevision.HasValue
    )
      return "Select a complete execution assignment reference.";
    if (draft.ArrivalTimeZone.Length > 0)
    {
      try
      {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(draft.ArrivalTimeZone);
        if (
          draft.ArrivalDate.HasValue
          && draft.ArrivalTime.HasValue
          && zone.IsInvalidTime(
            draft.ArrivalDate.Value.ToDateTime(draft.ArrivalTime.Value)
          )
        )
          return "The arrival time does not exist in this time zone.";
      }
      catch (TimeZoneNotFoundException)
      {
        return "Choose a supported arrival time zone.";
      }
      catch (InvalidTimeZoneException)
      {
        return "Choose a supported arrival time zone.";
      }
    }
    return null;
  }

  public static List<ShipmentIssue> Missing(BorderCrossing draft)
  {
    List<ShipmentIssue> issues = [];
    void Need(bool missing, string field, string message)
    {
      if (missing)
        issues.Add(new(field, message));
    }
    Need(
      draft.DestinationCountry.Length == 0,
      "DestinationCountry",
      "Choose Canada or United States."
    );
    Need(
      draft.PortOfEntry.Length == 0,
      "PortOfEntry",
      "Enter the port of entry."
    );
    Need(
      !draft.ArrivalDate.HasValue
        || !draft.ArrivalTime.HasValue
        || draft.ArrivalTimeZone.Length == 0,
      "Arrival",
      "Enter arrival date, time and time zone."
    );
    Need(
      draft.CarrierName.Length == 0,
      "CarrierName",
      "Enter the legal carrier name."
    );
    Need(
      draft.DestinationCountry == "US" && draft.Scac.Length == 0,
      "Scac",
      "Enter the SCAC."
    );
    Need(
      draft.DestinationCountry == "CA" && draft.CanadianCarrierCode.Length == 0,
      "CanadianCarrierCode",
      "Enter the Canadian carrier code."
    );
    Need(
      draft.EmptyConveyance && draft.Shipments.Count > 0,
      "Shipments",
      "An empty crossing cannot contain shipments."
    );
    Need(
      !draft.EmptyConveyance && draft.Shipments.Count == 0,
      "Shipments",
      "Select shipments or mark this crossing empty."
    );
    Need(
      !draft.Equipment.Any(x => x.Kind == "truck"),
      "Equipment",
      "Select a truck."
    );
    Need(
      !draft.Crew.Any(x => x.Role == "driver"),
      "Crew",
      "Select the crossing driver."
    );
    foreach (var row in draft.Shipments)
    {
      Need(
        row.Procedure != (draft.DestinationCountry == "CA" ? "PARS" : "PAPS"),
        $"Shipments.{row.Id}.Procedure",
        "Confirm the shipment procedure for this direction."
      );
      Need(
        draft.DestinationCountry == "CA"
          ? row.ParsNumber.Length == 0
          : row.PapsNumber.Length == 0,
        $"Shipments.{row.Id}.Reference",
        "Enter this shipment's PARS/PAPS number."
      );
      Need(
        !row.EquipmentId.HasValue,
        $"Shipments.{row.Id}.EquipmentId",
        "Select the equipment carrying this shipment."
      );
    }
    foreach (var row in draft.Crew)
      Need(
        row.Role != "passenger" && !row.DriverId.HasValue
          || row.Details.FirstName.Length == 0
          || row.Details.LastName.Length == 0
          || !row.Details.DateOfBirth.HasValue
          || row.Details.CitizenshipCountry.Length == 0,
        $"Crew.{row.Id}.Details",
        "Complete the person's legal identity."
      );
    foreach (var row in draft.Equipment.Where(x => x.Kind != "container"))
      Need(
        row.Kind == "truck" && !row.TruckId.HasValue
          || row.Kind == "trailer" && !row.TrailerId.HasValue
          || row.PlateNumber.Length == 0
          || row.PlateCountry.Length == 0
          || row.PlateRegion.Length == 0,
        $"Equipment.{row.Id}.Plate",
        "Complete the registration plate and issuing jurisdiction."
      );
    return issues;
  }

  private static bool Text(object value) =>
    value
      .GetType()
      .GetProperties()
      .Where(x => x.PropertyType == typeof(string))
      .All(x => x.GetValue(value) is string text && text.Length <= 300);

  private static bool Ids(IEnumerable<Guid> ids)
  {
    var rows = ids.ToArray();
    return rows.All(x => x != Guid.Empty)
      && rows.Distinct().Count() == rows.Length;
  }
}
