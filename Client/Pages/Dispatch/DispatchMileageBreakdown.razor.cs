using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Client.Models.DTO;
using Client.Models.DTO.Mileage;
using Client.Services;
using Client.Shared.Dispatch;
using Microsoft.AspNetCore.Components;

namespace Client.Pages.Dispatch;

public partial class DispatchMileageBreakdown : IDisposable
{
  [Inject]
  private ApiService Api { get; set; } = default!;

  [Parameter]
  public Guid DispatchId { get; set; }

  [Parameter]
  public Guid? DefaultTruckId { get; set; }

  [CascadingParameter]
  public DispatchSettingsState? DisplaySettings { get; set; }

  private CancellationTokenSource _identity = new();
  private CancellationTokenSource? _read;
  private DispatchMileageBreakdownState? _state;
  private MileageMovementRow? _editing;
  private MileageMovementRow? _distanceEditing;
  private readonly Dictionary<Guid, MileageMovementRow> _recent = new();
  private AllocationForm _form = new();
  private Guid _loadedId;
  private bool _expanded;
  private bool _loading;
  private bool _saving;
  private bool _saved;
  private bool _disposed;
  private bool _recording;
  private bool _editorBusy;
  private string? _notice;
  private string? _error;
  private string? _editError;
  private string PanelId => $"dispatch-mileage-{DispatchId}";
  private bool Writing => _saving || _editorBusy;
  private bool Busy => _loading || Writing;
  private bool EditorOpen =>
    _editing is not null || _recording || _distanceEditing is not null;
  private IEnumerable<MileageMovementRow> Rows =>
    (_state?.Movements ?? []).Concat(
      _recent.Values.Where(x =>
        !(_state?.Movements.Any(y => y.MovementId == x.MovementId) ?? false)
      )
    );
  private string SaveLabel => _saving ? "Saving…" : "Save allocation";

  protected override void OnParametersSet()
  {
    if (_loadedId == DispatchId)
      return;
    _identity.Cancel();
    _identity.Dispose();
    _identity = new();
    _read?.Cancel();
    _read = null;
    _loadedId = DispatchId;
    _state = null;
    _expanded = false;
    _loading = false;
    _saving = false;
    _saved = false;
    _recording = false;
    _editorBusy = false;
    _distanceEditing = null;
    _recent.Clear();
    _notice = null;
    _error = null;
    CancelEdit();
  }

  private async Task ToggleAsync()
  {
    if (Writing || EditorOpen)
      return;
    _expanded = !_expanded;
    if (_expanded && _state is null)
      await LoadAsync();
  }

  private async Task LoadAsync()
  {
    if (_disposed || Writing || DispatchId == Guid.Empty)
      return;
    _read?.Cancel();
    using var owner = CancellationTokenSource.CreateLinkedTokenSource(
      _identity.Token
    );
    _read = owner;
    var id = DispatchId;
    _loading = true;
    _error = null;
    var result = await Api.GetAsync<DispatchMileageBreakdownState>(
      $"api/dispatch/{id}/mileage-breakdown",
      owner.Token
    );
    if (
      _disposed
      || owner.IsCancellationRequested
      || owner != _read
      || id != DispatchId
    )
      return;
    _loading = false;
    _read = null;
    if (!result.Success || result.Response is null)
    {
      _error = result.ErrorMessage;
      return;
    }
    if (result.Response.DispatchId != id)
    {
      _error = "The mileage response does not match this load. Reload it.";
      return;
    }
    _state = result.Response;
  }

  private void BeginEdit(MileageMovementRow row)
  {
    if (Busy || EditorOpen || !row.Editable || !row.MovementId.HasValue)
      return;
    _editing = row;
    _form = new()
    {
      Target = row.ManualOverride ? row.AllocationTarget : "automatic",
    };
    _editError = null;
    _saved = false;
    _notice = null;
  }

  private void CancelEdit()
  {
    if (_saving)
      return;
    _editing = null;
    _form = new();
    _editError = null;
  }

  private async Task SaveAsync()
  {
    if (_disposed || _saving || _editing?.MovementId is not { } movementId)
      return;
    var id = DispatchId;
    var owner = _identity;
    var row = _editing;
    _saving = true;
    _editError = null;
    var result = await Api.PutAsync<
      MileageAllocationUpdate,
      MileageMovementRow
    >(
      $"api/mileage/movements/{movementId}/allocation",
      new(row.Revision, _form.Target, _form.Reason.Trim()),
      owner.Token
    );
    if (
      _disposed
      || owner != _identity
      || owner.IsCancellationRequested
      || id != DispatchId
    )
      return;
    _saving = false;
    if (!result.Success || result.Response is null)
    {
      _editError = result.ErrorMessage;
      return;
    }
    CancelEdit();
    Remember(result.Response);
    // A moved allocation must not leave the old load's totals on screen.
    _state = null;
    _saved = true;
    await LoadAsync();
  }

  private async Task ReloadAsync()
  {
    if (Writing)
      return;
    CancelEdit();
    _recording = false;
    _distanceEditing = null;
    await LoadAsync();
  }

  private void BeginRecording()
  {
    if (Busy || EditorOpen)
      return;
    _recording = true;
    _notice = null;
    _saved = false;
  }

  private void BeginDistance(MileageMovementRow row)
  {
    if (
      Busy
      || EditorOpen
      || !row.Editable
      || !row.MovementId.HasValue
      || !row.CanEditDistance
      || row.Origin != "manual"
    )
      return;
    _distanceEditing = row;
    _notice = null;
    _saved = false;
  }

  private void CancelRecording()
  {
    if (!Writing)
      _recording = false;
  }

  private void CancelDistance()
  {
    if (!Writing)
      _distanceEditing = null;
  }

  private void SetEditorBusy(bool value) => _editorBusy = value;

  private async Task MovementSavedAsync(MileageMovementRow row)
  {
    if (_disposed)
      return;
    _recording = false;
    Remember(row);
    _notice = "Movement saved. Add distance evidence when available.";
    if (row.AllocatedDispatchId != DispatchId)
      return;
    _state = null;
    await LoadAsync();
  }

  private async Task DistanceSavedAsync(MileageMovementRow row)
  {
    if (_disposed)
      return;
    _distanceEditing = null;
    Remember(row);
    _notice = "Distance evidence saved.";
    if (row.AllocatedDispatchId != DispatchId)
      return;
    _state = null;
    await LoadAsync();
  }

  private void Remember(MileageMovementRow row)
  {
    if (row.MovementId is { } id)
      _recent[id] = row;
  }

  private string TargetText(string target, MileageMovementRow row)
  {
    var number = target switch
    {
      "previous" => row.PreviousLoadNumber,
      "next" => row.NextLoadNumber,
      "carried" => row.CarriedLoadNumber,
      _ => null,
    };
    var label = target switch
    {
      "automatic" => "Use automatic allocation",
      "previous" => "Previous load",
      "next" => "Next load",
      "carried" => "Carried load",
      _ => "Unallocated",
    };
    var formatted = LoadNumberDisplay.Format(
      number,
      DisplaySettings?.LoadNumberPrefix
    );
    return number.HasValue ? $"{label} · {formatted}" : label;
  }

  private static IEnumerable<string> Targets(MileageMovementRow row)
  {
    yield return "automatic";
    if (row.PreviousDispatchId.HasValue)
      yield return "previous";
    if (row.NextDispatchId.HasValue)
      yield return "next";
    if (row.CarriedDispatchId.HasValue)
      yield return "carried";
    yield return "unallocated";
  }

  private static bool HasReason(MileageMovementRow row) =>
    row.ManualOverride && !string.IsNullOrWhiteSpace(row.AllocationReason);

  private static string Assignment(MileageMovementRow row) =>
    string.Join(
      " · ",
      new[]
      {
        string.IsNullOrWhiteSpace(row.TruckNumber)
          ? null
          : $"Truck {row.TruckNumber}",
        row.DriverName,
        row.CoDriverName,
        string.IsNullOrWhiteSpace(row.TrailerNumber)
          ? null
          : $"Trailer {row.TrailerNumber}",
      }.Where(x => !string.IsNullOrWhiteSpace(x))
    );

  private static bool HasLocations(MileageMovementRow row) =>
    !string.IsNullOrWhiteSpace(row.FromLocation)
    || !string.IsNullOrWhiteSpace(row.ToLocation);

  private static string Location(string? value) =>
    string.IsNullOrWhiteSpace(value) ? "Not recorded" : value;

  private static string Purpose(string value) =>
    value switch
    {
      "pickup-approach" => "Pickup approach",
      "yard-return" => "Yard return",
      "home" => "Home",
      "maintenance" => "Maintenance / repair",
      "reposition" => "Reposition",
      "loaded" => "Loaded movement",
      _ => Label(value),
    };

  private static string Label(string value) =>
    CultureInfo.InvariantCulture.TextInfo.ToTitleCase(
      value.Replace('-', ' ').Replace('_', ' ')
    );

  private static string Timestamp(DateTime? value) =>
    value?.ToString("MMM d · hh:mm tt 'UTC'", CultureInfo.InvariantCulture)
    ?? "No timestamp";

  public void Dispose()
  {
    _disposed = true;
    _identity.Cancel();
    _identity.Dispose();
    _read?.Cancel();
    _read = null;
    _editing = null;
    _form = new();
  }

  private sealed class AllocationForm
  {
    [Required]
    public string Target { get; set; } = "automatic";

    [Required, StringLength(400)]
    public string Reason { get; set; } = "";
  }
}
