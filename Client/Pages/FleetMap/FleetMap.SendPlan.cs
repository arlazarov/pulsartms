namespace Client.Pages.FleetMap;

// The Send plan window: this shift's fuel as it goes to the driver. It is
// one of the map's overlays, so it hides the inspector while open and
// never opens over the fuel editor, the route editor or the camera.
public partial class FleetMap
{
  private bool _sendPlanOpen;
  private FuelEditorIdentity? _sendPlanIdentity;

  private bool CanSendPlan =>
    CanEditFuel
    && !_fuelEditorOpen
    && !_sendPlanOpen
    && !_recalculatingFuel
    && _routeEditorDispatch is null
    && !_cameraOpen
    && _routeState?.Plan?.FuelPlan is not null;

  private async Task OpenSendPlanAsync()
  {
    if (!CanSendPlan)
      return;
    _sendPlanIdentity = new(
      _activeTruckId!.Value,
      SelectedDispatchId!.Value,
      SelectedExecutionLegId,
      SelectedAssignmentRevision
    );
    _sendPlanOpen = true;
    await PublishInspectorSuspensionAsync();
  }

  private async Task CloseSendPlanAsync(FuelEditorIdentity identity)
  {
    if (_disposed || _sendPlanIdentity != identity)
      return;
    _sendPlanOpen = false;
    _sendPlanIdentity = null;
    await PublishInspectorSuspensionAsync();
  }

  // The server records the hand-over and asks for this truck's summary
  // again; one ordinary read brings the badge to the map now rather than on
  // the next poll.
  private async Task OnFuelPlanSentAsync(FuelEditorIdentity identity)
  {
    if (
      _disposed
      || _sendPlanIdentity != identity
      || _activeTruckId != identity.Truck
    )
      return;
    await LoadRouteAsync(false, force: true);
  }
}
