using Client.Models.DTO.Border;
using Client.Models.DTO.Mileage;
using Microsoft.AspNetCore.Components;

namespace Client.Pages.Border;

public partial class BorderResources
{
  [Parameter, EditorRequired]
  public BorderCrossing Value { get; set; } = new();

  [Parameter]
  public bool Disabled { get; set; }

  [Parameter]
  public EventCallback Changed { get; set; }

  [Parameter]
  public EventCallback AssignmentChanged { get; set; }

  [Parameter]
  public List<MileageDriverOption> Drivers { get; set; } = [];

  [Parameter]
  public List<MileageUnitOption> Trucks { get; set; } = [];

  [Parameter]
  public List<MileageUnitOption> Trailers { get; set; } = [];

  private Task NotifyChanged() => Changed.InvokeAsync();

  private async Task DriverSelected(BorderCrew person)
  {
    person.Details = new();
    person.DisplayName =
      Drivers.FirstOrDefault(x => x.Id == person.DriverId)?.Name ?? "";
    await AssignmentChanged.InvokeAsync();
  }

  private async Task VehicleSelected(BorderEquipment vehicle)
  {
    vehicle.UnitNumber =
      vehicle.Kind == "truck"
        ? Trucks.FirstOrDefault(x => x.Id == vehicle.TruckId)?.UnitNumber ?? ""
        : Trailers.FirstOrDefault(x => x.Id == vehicle.TrailerId)?.UnitNumber
          ?? "";
    vehicle.PlateNumber =
      vehicle.PlateRegion =
      vehicle.PlateCountry =
      vehicle.Vin =
        "";
    await AssignmentChanged.InvokeAsync();
  }

  private async Task AddPerson(string role)
  {
    if (Disabled)
      return;
    Value.Crew.Add(new() { Id = Guid.NewGuid(), Role = role });
    await AssignmentChanged.InvokeAsync();
  }

  private async Task RemovePerson(BorderCrew person)
  {
    if (Disabled)
      return;
    Value.Crew.Remove(person);
    await AssignmentChanged.InvokeAsync();
  }

  private async Task AddDocument(BorderCrew person)
  {
    if (Disabled)
      return;
    person.Details.Documents.Add(new() { Id = Guid.NewGuid() });
    await NotifyChanged();
  }

  private async Task RemoveDocument(
    BorderCrew person,
    BorderTravelDocument document
  )
  {
    if (Disabled)
      return;
    person.Details.Documents.Remove(document);
    await NotifyChanged();
  }

  private async Task AddVehicle(string kind)
  {
    if (Disabled)
      return;
    Value.Equipment.Add(new() { Id = Guid.NewGuid(), Kind = kind });
    await AssignmentChanged.InvokeAsync();
  }

  private async Task RemoveVehicle(BorderEquipment vehicle)
  {
    if (Disabled)
      return;
    Value.Equipment.Remove(vehicle);
    foreach (
      var shipment in Value.Shipments.Where(x => x.EquipmentId == vehicle.Id)
    )
      shipment.EquipmentId = null;
    await AssignmentChanged.InvokeAsync();
  }
}
