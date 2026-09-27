using System.Globalization;
using Client.Models.DTO.Fleet;
using Client.Shared.Trucks;
using Microsoft.AspNetCore.Components;

namespace Client.Pages.FleetMap;

public partial class FleetTruckList
{
  [Parameter]
  public IReadOnlyList<TruckLocationMapDto> Trucks { get; set; } = [];

  [Parameter]
  public int Total { get; set; }

  [Parameter]
  public Guid? SelectedId { get; set; }

  [Parameter]
  public EventCallback<Guid> Selected { get; set; }

  [Parameter]
  public bool Collapsed { get; set; }

  [Parameter]
  public EventCallback<bool> CollapsedChanged { get; set; }

  private static string SpeedText(TruckLocationMapDto truck) =>
    $"{truck.Speed.ToString("0", CultureInfo.InvariantCulture)} mph";

  // The number the truck is said to pull, or the one another source names
  // for it when none is reported; nothing is chosen between them here.
  private static string Trailer(TruckLocationMapDto truck) =>
    Text(
      string.IsNullOrWhiteSpace(truck.TrailerNumber)
        ? truck.TrailerConflictNumber
        : truck.TrailerNumber
    );

  private static string Text(string? value) =>
    string.IsNullOrWhiteSpace(value) ? "—" : value;
}
