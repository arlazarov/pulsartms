using System.Globalization;
using Client.Models.DTO.Fleet;
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

  private static string SpeedText(TruckLocationMapDto truck) =>
    $"{truck.Speed.ToString("0", CultureInfo.InvariantCulture)} mph";

  private static string Text(string? value) =>
    string.IsNullOrWhiteSpace(value) ? "—" : value;
}
