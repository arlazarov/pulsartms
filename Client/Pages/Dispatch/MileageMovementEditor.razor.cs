using System.ComponentModel.DataAnnotations;
using System.Net;
using Client.Models.DTO.Mileage;
using Client.Services;
using Microsoft.AspNetCore.Components;

namespace Client.Pages.Dispatch;

public partial class MileageMovementEditor : IDisposable
{
  [Inject]
  private ApiService Api { get; set; } = default!;

  [Inject]
  private TimeProvider Clock { get; set; } = default!;

  [Parameter]
  public Guid DispatchId { get; set; }

  [Parameter]
  public Guid? DefaultTruckId { get; set; }

  [Parameter]
  public EventCallback<MileageMovementRow> Saved { get; set; }

  [Parameter]
  public EventCallback Cancelled { get; set; }

  [Parameter]
  public EventCallback<bool> BusyChanged { get; set; }

  private readonly CancellationTokenSource _lifetime = new();
  private readonly Guid _key = Guid.NewGuid();
  private readonly MovementForm _form = new();
  private readonly MileageTimeDraft _start = new();
  private readonly MileageTimeDraft _end = new();
  private List<MileageUnitOption>? _trucks;
  private List<MileageUnitOption>? _trailers;
  private List<MileageDriverOption>? _drivers;
  private RecordMovementRequest? _submitted;
  private bool _loading;
  private bool _saving;
  private bool _disposed;
  private string? _error;
  private string? _loadError;
  private bool Locked => _saving || _submitted is not null;
  private string FieldId => $"movement-{_key}";

  protected override async Task OnInitializedAsync()
  {
    _form.TruckId = DefaultTruckId;
    await LoadFleetAsync();
  }

  private async Task LoadFleetAsync()
  {
    if (_loading || _disposed)
      return;
    _loading = true;
    _loadError = null;
    var trucks = Api.GetAsync<MileageFleetList<MileageUnitOption>>(
      "api/fleet/trucks",
      _lifetime.Token
    );
    var trailers = Api.GetAsync<MileageFleetList<MileageUnitOption>>(
      "api/fleet/trailers",
      _lifetime.Token
    );
    var drivers = Api.GetAsync<MileageFleetList<MileageDriverOption>>(
      "api/fleet/drivers",
      _lifetime.Token
    );
    await Task.WhenAll(trucks, trailers, drivers);
    if (_disposed)
      return;
    var truckResult = await trucks;
    var trailerResult = await trailers;
    var driverResult = await drivers;
    _loading = false;
    if (!truckResult.Success || truckResult.Response is null)
      _loadError = truckResult.ErrorMessage;
    else if (!trailerResult.Success || trailerResult.Response is null)
      _loadError = trailerResult.ErrorMessage;
    else if (!driverResult.Success || driverResult.Response is null)
      _loadError = driverResult.ErrorMessage;
    else
    {
      _trucks = truckResult.Response.Items;
      _trailers = trailerResult.Response.Items;
      _drivers = driverResult.Response.Items;
    }
  }

  private async Task SaveAsync()
  {
    if (_saving || _disposed || _trucks is null)
      return;
    _error = null;
    if (_submitted is null)
    {
      if (!Validate(out var start, out var end))
        return;
      _submitted = new(
        _key,
        _form.TruckId!.Value,
        _form.DriverId,
        _form.CoDriverId,
        _form.TrailerId,
        null,
        _form.Purpose,
        _form.CargoState,
        _form.Context == "after" ? DispatchId : null,
        _form.Context == "toward" ? DispatchId : null,
        _form.CargoState == "loaded" ? DispatchId : null,
        _form.From.Trim(),
        _form.To.Trim(),
        start,
        end
      );
    }
    _saving = true;
    await BusyChanged.InvokeAsync(true);
    var result = await Api.PostAsync<RecordMovementRequest, MileageMovementRow>(
      "api/mileage/movements",
      _submitted,
      _lifetime.Token
    );
    if (_disposed)
      return;
    _saving = false;
    await BusyChanged.InvokeAsync(false);
    if (!result.Success || result.Response is null)
    {
      _error = result.ErrorMessage;
      // Definitive rejection is safe to edit; an uncertain result is retried
      // with exactly the same key and payload to avoid duplicate movements.
      if (
        result.HttpStatusCode
        is HttpStatusCode.BadRequest
          or HttpStatusCode.Forbidden
          or HttpStatusCode.Unauthorized
          or HttpStatusCode.Conflict
      )
        _submitted = null;
      return;
    }
    await Saved.InvokeAsync(result.Response);
  }

  private bool Validate(out DateTimeOffset? start, out DateTimeOffset? end)
  {
    start = null;
    end = null;
    if (_form.TruckId is null || _form.TruckId == Guid.Empty)
      _error = "Select the truck that made this movement.";
    else if (_form.DriverId.HasValue && _form.DriverId == _form.CoDriverId)
      _error = "Driver and co-driver must be different.";
    else if (_form.CargoState == "bobtail" && _form.TrailerId.HasValue)
      _error = "Bobtail means no trailer. Clear the trailer selection.";
    else if (
      _form.CargoState is "empty" or "loaded"
      && !_form.TrailerId.HasValue
    )
      _error = "Select the trailer for this movement.";
    else if (
      !_start.TryRead(out start)
      || !_end.TryRead(out end)
      || start.HasValue != end.HasValue
    )
      _error = "Enter both actual dates and times, or leave both blank.";
    else if (
      start.HasValue
      && (start >= end || end > Clock.GetUtcNow() || start.Value.Year < 2000)
    )
      _error = "Actual start must precede end; neither can be in the future.";
    return _error is null;
  }

  private async Task CancelAsync()
  {
    if (!_saving)
      await Cancelled.InvokeAsync();
  }

  private static string Unit(MileageUnitOption option) =>
    option.UnitNumber + (option.IsActive ? "" : " (inactive)");

  private static string Driver(MileageDriverOption option) =>
    option.Name + (option.IsActive ? "" : " (inactive)");

  public void Dispose()
  {
    _disposed = true;
    _lifetime.Cancel();
    _lifetime.Dispose();
  }

  private sealed class MovementForm
  {
    [Required]
    public Guid? TruckId { get; set; }
    public Guid? DriverId { get; set; }
    public Guid? CoDriverId { get; set; }
    public Guid? TrailerId { get; set; }
    public string Purpose { get; set; } = "home";
    public string CargoState { get; set; } = "unknown";
    public string Context { get; set; } = "after";

    [Required, StringLength(500)]
    public string From { get; set; } = "";

    [Required, StringLength(500)]
    public string To { get; set; } = "";
  }
}
