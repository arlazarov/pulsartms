using Client.Models.DTO.Planning;
using Microsoft.AspNetCore.Components;

namespace Client.Shared.DriverStatus.StopHours;

public partial class StopHours
{
  [Inject]
  private TimeProvider Clock { get; set; } = default!;

  [Parameter, EditorRequired]
  public StopEta Estimate { get; set; } = default!;

  [Parameter]
  public bool ShowRecap { get; set; }

  [Parameter]
  public bool ShowSummary { get; set; } = true;

  [Parameter]
  public bool ShowDetails { get; set; } = true;

  [Parameter]
  public StopCycleForecast? Recap { get; set; }

  // How the same facts are read where they stand: "inline" runs the ETA as
  // one line, "compact" is the quieter reading a card on the map uses. The
  // stylesheet that owns the component owns both; a page asks for one
  // instead of restyling what is inside.
  [Parameter]
  public string Reading { get; set; } = string.Empty;
  private string ReadingClass =>
    Reading.Length > 0 ? $"stop-hours stop-hours--{Reading}" : "stop-hours";

  // A stop may arrive with no hours behind it: an older dispatch, or one
  // the server could not forecast. The hour and whether it is late are still
  // worth saying, and are said here rather than copied into a second block.
  private bool HasForecast => Estimate.Hours is not null;
  private StopHoursForecast Hours => Estimate.Hours!;
  private int? RemainingCycle =>
    Hours.CycleAtArrivalMinutes ?? Hours.CurrentCycleMinutes;
  private bool CycleKnown => Hours.CycleVerified && RemainingCycle.HasValue;
  private string CycleTitle =>
    Hours.CycleAtArrivalMinutes.HasValue
      ? "Estimated cycle remaining on arrival"
      : "Current cycle remaining at this stop";
  private bool CycleShort => StopHoursDisplay.CycleShort(Hours);
  private string Status =>
    Estimate.LateMinutes is > 0
      ? $"Late by {StopHoursDisplay.Lateness(Estimate.LateMinutes.Value)}"
    : Estimate.LateMinutes == 0 && (!HasForecast || (CycleKnown && !CycleShort))
      ? "On time"
    : "";
  private string StatusTone =>
    Estimate.LateMinutes is > 0 ? "danger" : "success";
  private string CycleStatus =>
    !HasForecast ? ""
    : !CycleKnown ? "Cycle unknown"
    : CycleShort ? "Cycle short"
    : "";
  private string CycleStatusTone => !CycleKnown ? "muted" : "danger";
  private IEnumerable<StopHoursAlternative> Alternatives =>
    Hours.CycleVerified && Estimate.Appointment.HasValue
      ? Hours
        .Alternatives.Where(alternative =>
          alternative.LateMinutes is >= 0 && alternative.Kind == "recap"
        )
        .DistinctBy(alternative => alternative.Kind)
      : [];
  private DateTimeOffset? NextRecap =>
    Recap is { RecapVerified: true, NextRecapMinutes: > 0, NextRecapAt: { } at }
    && at > Clock.GetUtcNow()
      ? at
      : null;
  private string RecapStatus =>
    Recap is null ? "—"
    : !Recap.RecapVerified ? "Unknown"
    : Recap.NextRecapAt is { } at && at <= Clock.GetUtcNow() ? "—"
    : Recap.NextRecapMinutes is > 0 ? "Unknown"
    : "—";

  private string CycleValue(int? minutes) =>
    StopHoursDisplay.Signed(Hours.CycleVerified ? minutes : null);

  private string CycleClass(int? minutes) =>
    !Hours.CycleVerified ? "stop-hours__value stop-hours__value--muted"
    : minutes is < 0 ? "stop-hours__value stop-hours__value--danger"
    : "stop-hours__value";

  private string AlternativeStatus(StopHoursAlternative alternative) =>
    alternative.LateMinutes == 0 ? "On time with recap"
    : alternative.LateMinutes is > 0
      ? $"Late by {StopHoursDisplay.Lateness(alternative.LateMinutes.Value)}"
    : "With recap";
}
