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

  // A truck's current load and the stop it is heading for, when known.
  [Parameter]
  public Func<Guid, FleetTruckWork>? Work { get; set; }

  [Parameter]
  public EventCallback<bool> CollapsedChanged { get; set; }

  private static string SpeedText(TruckLocationMapDto truck) =>
    $"{truck.Speed.ToString("0", CultureInfo.InvariantCulture)} mph";

  private static string Text(string? value) =>
    string.IsNullOrWhiteSpace(value) ? "—" : value;
}

public readonly record struct FleetTruckWork(int? LoadNumber, string? NextStop);
