using Client.Models.DTO.Dispatch;
using Client.Models.DTO.Fleet;
using Client.Shared.Dispatch;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Client.Pages.FleetMap;

public partial class FleetTripChain : IAsyncDisposable
{
  [Inject]
  private IJSRuntime JS { get; set; } = default!;

  private ElementReference _links;
  private ElementReference? _bound;
  private IJSObjectReference? _module;
  private IJSObjectReference? _wheel;

  [Parameter]
  public TruckLocationMapDto? Truck { get; set; }

  [Parameter]
  public IReadOnlyList<DispatchResponse> Loads { get; set; } = [];

  [Parameter]
  public bool Loading { get; set; }

  [Parameter]
  public bool Failed { get; set; }

  // The load the page's route and truck card are for.
  [Parameter]
  public Guid? CurrentId { get; set; }

  // The trip the panel under the truck shows.
  [Parameter]
  public DispatchResponse? SelectedTrip { get; set; }

  [Parameter]
  public EventCallback<DispatchResponse> Selected { get; set; }

  // Where a card's load opens, returning to this map.
  [Parameter]
  public Func<DispatchResponse, string>? LoadHref { get; set; }

  private bool IsSelected(DispatchResponse load) =>
    SelectedTrip is { } trip
    && trip.Id == load.Id
    && trip.ExecutionLegId == load.ExecutionLegId;

  // The colour is the server's phase; an unplaced or stale load stays
  // neutral rather than borrowing a place.
  private static string PhaseClass(DispatchResponse load) =>
    load.Completed
      ? "is-completed"
      : load.WorkPhase switch
      {
        "current" => "is-current",
        "next" => "is-next",
        "upcoming" => "is-upcoming",
        _ => "is-unplaced",
      };

  // The list is rebuilt when the truck changes; the wheel follows it. A
  // bind that finishes after the component is gone is released at once.
  private bool _disposed;

  protected override async Task OnAfterRenderAsync(bool firstRender)
  {
    if (_disposed || _links.Context is null || _bound?.Id == _links.Id)
      return;
    var target = _links;
    _bound = target;
    try
    {
      var module = _module;
      if (module is null)
      {
        // Held here until it is known the component still wants it: a
        // disposal during the import must not leave it behind.
        var imported = await JS.InvokeAsync<IJSObjectReference>(
          "import",
          "./js/generated/shared/horizontalWheel.js"
        );
        if (_disposed)
        {
          await imported.DisposeAsync();
          return;
        }
        module = _module ??= imported;
        if (!ReferenceEquals(module, imported))
          await imported.DisposeAsync();
      }
      await ReleaseWheelAsync();
      if (_disposed)
        return;
      var wheel = await module.InvokeAsync<IJSObjectReference>(
        "bindHorizontalWheel",
        target
      );
      if (_disposed || _bound?.Id != target.Id)
      {
        await wheel.InvokeVoidAsync("dispose");
        await wheel.DisposeAsync();
        return;
      }
      _wheel = wheel;
    }
    catch (JSException) { }
    catch (JSDisconnectedException) { }
  }

  private async Task ReleaseWheelAsync()
  {
    var wheel = _wheel;
    _wheel = null;
    if (wheel is null)
      return;
    await wheel.InvokeVoidAsync("dispose");
    await wheel.DisposeAsync();
  }

  public async ValueTask DisposeAsync()
  {
    _disposed = true;
    try
    {
      await ReleaseWheelAsync();
      var module = _module;
      _module = null;
      if (module is not null)
        await module.DisposeAsync();
    }
    catch (JSException) { }
    catch (JSDisconnectedException) { }
  }
}
