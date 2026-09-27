using Client.Models.DTO.Dispatch;
using Client.Models.DTO.Planning;
using Client.Shared.Dispatch;
using Microsoft.AspNetCore.Components;

namespace Client.Pages.FleetMap;

public partial class FleetTripDetail
{
  [Parameter, EditorRequired]
  public DispatchResponse Load { get; set; } = default!;

  [Parameter]
  public TruckDispatchBoardResponse? Truck { get; set; }

  [Parameter]
  public int Position { get; set; } = 1;

  [Parameter]
  public int Count { get; set; } = 1;

  // The trip's ETA: the page's own for the current trip, the board's for
  // the others; never computed here.
  [Parameter]
  public DispatchEta? Eta { get; set; }

  [Parameter]
  public bool Refreshing { get; set; }

  [Parameter]
  public Guid? FocusedStopId { get; set; }

  [Parameter]
  public EventCallback<Guid?> StopChosen { get; set; }

  [Parameter]
  public EventCallback<DispatchResponse> RouteOptions { get; set; }

  private readonly Dictionary<Guid, ElementReference> _stops = [];
  private Guid? _shown;

  private DispatchBoardRow Row => new(Truck ?? new(), Load);

  private string PhaseClass =>
    Load.WorkPhase switch
    {
      "current" => "is-current",
      "next" => "is-next",
      "upcoming" => "is-upcoming",
      _ => "is-unplaced",
    };

  // The chosen stop is brought into view once each time it is chosen.
  protected override async Task OnAfterRenderAsync(bool firstRender)
  {
    if (FocusedStopId == _shown)
      return;
    _shown = FocusedStopId;
    if (FocusedStopId is { } id && _stops.TryGetValue(id, out var element))
      await element.FocusAsync();
  }
}
