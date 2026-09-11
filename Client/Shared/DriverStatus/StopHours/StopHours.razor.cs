using Client.Models.DTO.Planning;
using Microsoft.AspNetCore.Components;

namespace Client.Shared.DriverStatus.StopHours;

public partial class StopHours
{
    [Inject] private TimeProvider Clock { get; set; } = default!;
    [Parameter, EditorRequired] public StopEta Estimate { get; set; } = default!;
    [Parameter] public bool ShowRecap { get; set; }
    [Parameter] public bool ShowSummary { get; set; } = true;
    [Parameter] public bool ShowDetails { get; set; } = true;
    [Parameter] public StopCycleForecast? Recap { get; set; }
    private StopHoursForecast Hours => Estimate.Hours!;
    private bool CycleKnown => Hours.CycleVerified && Hours.CycleAtArrivalMinutes.HasValue;
    private bool CycleShort => StopHoursDisplay.CycleShort(Hours);
    private string Status => Estimate.LateMinutes is > 0
        ? $"Late by {StopHoursDisplay.Duration(Estimate.LateMinutes.Value)}"
        : Estimate.LateMinutes == 0 && CycleKnown && !CycleShort ? "On time" : "";
    private string StatusTone => Estimate.LateMinutes is > 0 ? "danger" : "success";
    private string CycleStatus => !CycleKnown ? "Cycle unknown"
        : CycleShort ? "Cycle short" : "";
    private string CycleStatusTone => !CycleKnown ? "muted" : "danger";
    private IEnumerable<StopHoursAlternative> Alternatives => Hours.CycleVerified && Estimate.Appointment.HasValue
        ? Hours.Alternatives.Where(alternative => alternative.LateMinutes is >= 0 && alternative.Kind == "recap")
            .DistinctBy(alternative => alternative.Kind)
        : [];
    private DateTimeOffset? NextRecap => Recap is { RecapVerified: true, NextRecapMinutes: > 0, NextRecapAt: { } at }
        && at > Clock.GetUtcNow() ? at : null;
    private string RecapStatus => Recap is null ? "—" : !Recap.RecapVerified ? "Unknown"
        : Recap.NextRecapAt is { } at && at <= Clock.GetUtcNow() ? "—"
        : Recap.NextRecapMinutes is > 0 ? "Unknown" : "—";
    private string CycleValue(int? minutes) => StopHoursDisplay.Signed(Hours.CycleVerified ? minutes : null);
    private string CycleClass(int? minutes) => !Hours.CycleVerified
        ? "stop-hours__value stop-hours__value--muted" : minutes is < 0
        ? "stop-hours__value stop-hours__value--danger" : "stop-hours__value";
    private string AlternativeStatus(StopHoursAlternative alternative) => alternative.LateMinutes == 0 ? "On time with recap"
        : alternative.LateMinutes is > 0
            ? $"Late by {StopHoursDisplay.Duration(alternative.LateMinutes.Value)}"
            : "With recap";
}
