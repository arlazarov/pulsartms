using System.ComponentModel.DataAnnotations;
using Client.Models;
using Client.Models.DTO.Mileage;
using Client.Services;
using Microsoft.AspNetCore.Components;

namespace Client.Pages.Dispatch;

public partial class MileageDistanceEditor : IDisposable
{
  [Inject]
  private ApiService Api { get; set; } = default!;

  [Inject]
  private TimeProvider Clock { get; set; } = default!;

  [Parameter, EditorRequired]
  public MileageMovementRow Movement { get; set; } = default!;

  [Parameter]
  public EventCallback<MileageMovementRow> Saved { get; set; }

  [Parameter]
  public EventCallback Cancelled { get; set; }

  [Parameter]
  public EventCallback<bool> BusyChanged { get; set; }

  [CascadingParameter]
  public DisplayUnits Units { get; set; } = DisplayUnits.Default;

  private readonly CancellationTokenSource _lifetime = new();
  private readonly DistanceForm _form = new();
  private MileageTimeDraft _start = new();
  private MileageTimeDraft _end = new();
  private MileageTimeDraft _observed = new();
  private bool _kilometers;
  private bool _saving;
  private bool _disposed;
  private string? _error;
  private string FieldId => $"distance-{Movement.MovementId}";
  private bool Actual => _form.Source != "manual-estimate";
  private string Unit => _kilometers ? "km" : "mi";

  protected override void OnInitialized()
  {
    _kilometers = Units.Distance == "kilometers";
    _start = MileageTimeDraft.From(Movement.StartedAt);
    _end = MileageTimeDraft.From(Movement.EndedAt);
    _observed = MileageTimeDraft.From(Clock.GetUtcNow().UtcDateTime);
  }

  private async Task SaveAsync()
  {
    if (
      _disposed
      || _saving
      || Movement.MovementId is not { } id
      || !Movement.CanEditDistance
      || Movement.Origin != "manual"
    )
      return;
    _error = null;
    var request = Request();
    if (request is null)
      return;
    _saving = true;
    await BusyChanged.InvokeAsync(true);
    var result = await Api.PutAsync<MovementDistanceUpdate, MileageMovementRow>(
      $"api/mileage/movements/{id}/distance",
      request,
      _lifetime.Token
    );
    if (_disposed)
      return;
    _saving = false;
    await BusyChanged.InvokeAsync(false);
    if (!result.Success || result.Response is null)
    {
      _error = result.ErrorMessage;
      return;
    }
    await Saved.InvokeAsync(result.Response);
  }

  private MovementDistanceUpdate? Request()
  {
    if (
      !_observed.TryRead(out var observed)
      || observed is null
      || observed > Clock.GetUtcNow()
      || observed.Value.Year < 2000
    )
    {
      _error = "Enter when this evidence was observed, in your local time.";
      return null;
    }
    DateTimeOffset? start = null;
    DateTimeOffset? end = null;
    if (Actual)
    {
      // Confirmed UTC boundaries must not be reinterpreted through an
      // ambiguous local clock during a daylight-saving transition.
      start = Utc(Movement.StartedAt);
      end = Utc(Movement.EndedAt);
      if (
        start is null && !_start.TryRead(out start)
        || end is null && !_end.TryRead(out end)
      )
      {
        _error = "Enter valid actual start and end dates and times.";
        return null;
      }
      if (
        start is null
        || end is null
        || start >= end
        || end > observed
        || start.Value.Year < 2000
      )
      {
        _error = "Actual start must precede end and evidence observation.";
        return null;
      }
    }
    if (_form.Distance is not { } distance || distance < 0)
    {
      _error = "Enter a nonnegative distance from the selected evidence.";
      return null;
    }
    var miles = decimal.Round(
      _kilometers ? distance / 1.609344m : distance,
      3,
      MidpointRounding.AwayFromZero
    );
    if (miles > 1_000_000)
    {
      _error = "Distance cannot exceed 1,000,000 miles.";
      return null;
    }
    return new(
      Movement.Revision,
      Actual ? "actual" : "planned",
      miles,
      _form.Source,
      _form.Reference.Trim(),
      observed.Value,
      _form.Reason.Trim(),
      start,
      end
    );
  }

  private async Task CancelAsync()
  {
    if (!_saving)
      await Cancelled.InvokeAsync();
  }

  private static DateTimeOffset? Utc(DateTime? value) =>
    value is { } time
      ? new(DateTime.SpecifyKind(time, DateTimeKind.Utc))
      : null;

  public void Dispose()
  {
    _disposed = true;
    _lifetime.Cancel();
    _lifetime.Dispose();
  }

  private sealed class DistanceForm
  {
    public string Source { get; set; } = "manual-estimate";

    [Required]
    public decimal? Distance { get; set; }

    [Required, StringLength(300)]
    public string Reference { get; set; } = "";

    [Required, StringLength(500)]
    public string Reason { get; set; } = "";
  }
}
