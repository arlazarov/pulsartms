using System.Net;
using Client.Models.DTO.Dispatch;
using Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Client.Shared.Dispatch.TruckAssignmentEditor;

public partial class TruckAssignmentEditor
{
  [Inject]
  private ApiService Api { get; set; } = default!;

  [Inject]
  private IJSRuntime JS { get; set; } = default!;

  [Parameter, EditorRequired]
  public DispatchResponse Load { get; set; } = default!;

  [Parameter]
  public EventCallback Changed { get; set; }
  private readonly CancellationTokenSource _lifetime = new();
  private bool _editing,
    _saving,
    _disposed;
  private string _truckNumber = "";
  private Guid? _stopId;
  private Guid _loadId;
  private long _revision;
  private string? _error;
  private Dictionary<Guid, string> _identities = [];

  protected override void OnParametersSet()
  {
    if (_editing && _loadId != Load.Id)
      Cancel();
  }

  private void Begin()
  {
    _loadId = Load.Id;
    _revision = Load.PlanningAssignmentRevision;
    _truckNumber = Load.TruckNumber;
    _stopId = Load.PlanningFromStopId;
    _identities = Load.Stops.ToDictionary(s => s.Id, s => s.CompletionIdentity);
    _editing = true;
    _error = null;
  }

  private void Cancel()
  {
    _editing = false;
    _error = null;
  }

  private async Task SaveAsync(bool reset)
  {
    if (_saving || !_editing)
      return;
    if (!reset && (_stopId is null || string.IsNullOrWhiteSpace(_truckNumber)))
    {
      _error = "Choose the truck and its starting stop.";
      return;
    }
    var id = _loadId;
    _saving = true;
    _error = null;
    try
    {
      var result = await Api.PutAsync<
        TruckAssignmentUpdate,
        TruckAssignmentState
      >(
        $"api/dispatch/{id}/truck-assignment",
        new(
          reset ? null : _truckNumber.Trim(),
          reset ? null : _stopId,
          _revision,
          _stopId is { } stop ? _identities.GetValueOrDefault(stop) : null
        ),
        _lifetime.Token
      );
      if (_disposed || Load.Id != id)
        return;
      if (!result.Success || result.Response is not { } state)
      {
        await JS.InvokeVoidAsync(
          "console.error",
          "Truck assignment failed",
          id,
          result.HttpStatusCode
        );
        _error =
          result.HttpStatusCode == HttpStatusCode.Conflict
            ? "Assignments or stops changed. Reopen the load and review the imported truck assignments."
            : "Could not save the assignment. Check the truck number and your access, then try again.";
        return;
      }
      Load.PlanningTruckId = state.TruckId;
      Load.PlanningFromStopId = state.FromStopId;
      Load.PlanningAssignmentRevision = state.Revision;
      Load.PlanningAssignmentRecordedAt = state.RecordedAt;
      _editing = false;
      await Changed.InvokeAsync();
    }
    finally
    {
      _saving = false;
    }
  }

  public void Dispose()
  {
    _disposed = true;
    _lifetime.Cancel();
    _lifetime.Dispose();
  }
}
