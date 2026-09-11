using Client.Models.DTO.Dispatch;
using Client.Models.DTO.Planning;
using Client.Services;
using Microsoft.AspNetCore.Components;

namespace Client.Shared.Dispatch.DispatchLoadStop;

public partial class DispatchLoadStop
{
    [Inject] private TimeProvider Clock { get; set; } = default!;
    [Parameter] public Guid DispatchId { get; set; }
    [Parameter, EditorRequired] public DispatchStopResponse Stop { get; set; } = default!;
    [Parameter] public int Number { get; set; }
    [Parameter] public DateOnly? FallbackDate { get; set; }
    [Parameter] public DispatchEta? Eta { get; set; }
    [Parameter] public bool Refreshing { get; set; }
    [Parameter] public bool CompletedLoad { get; set; }
    [Parameter] public bool Detailed { get; set; } = true;
    [Parameter] public bool CollapsibleDetails { get; set; }
    [Parameter] public RenderFragment? AdditionalDetails { get; set; }
    private ArrivalDisplayMemory Memory { get; } = new();
    private PlanStop PlannedStop { get; set; } = default!;
    private DispatchEta? StopEstimate { get; set; }
    private DispatchEta? DisplayEstimate => Completed ? null : Memory.Display(StopEstimate, Clock.GetUtcNow().UtcDateTime, Refreshing);
    private bool HasDisplayEstimate => DisplayEstimate?.Stops.Count > 0;
    private bool HasHoursForecast => DisplayEstimate?.Stops.FirstOrDefault()?.Hours is not null;
    private bool HasLegacyCycle => DisplayEstimate?.Stops.FirstOrDefault()?.CycleAfterDeparture?.RemainingMinutes is >= 0;
    private string CycleRemaining => DispatchCycleDisplay.Remaining(DisplayEstimate?.Stops.FirstOrDefault()?.CycleAfterDeparture);
    private bool Completed => CompletedLoad || (Stop.DepartedAt ?? Stop.DeliveredAt ?? Stop.PickedUpAt).HasValue;
    private string? _referenceNotes, _referenceJob;
    private IReadOnlyList<string> AppointmentReferences { get; set; } = [];
    private bool HasStreet => !string.IsNullOrWhiteSpace(Stop.City) && !string.IsNullOrWhiteSpace(Stop.Address);
    private bool HasDetails => HasStreet || !string.IsNullOrWhiteSpace(Stop.StopNo)
        || AppointmentReferences.Count > 0 || HasDisplayEstimate || AdditionalDetails is not null;
    private string Job => string.IsNullOrWhiteSpace(Stop.Job) ? "Stop" : Stop.Job;
    private string Location => !string.IsNullOrWhiteSpace(Stop.City)
        ? string.Join(", ", new[] { Stop.City, Stop.Province }.Where(value => !string.IsNullOrWhiteSpace(value)))
        : !string.IsNullOrWhiteSpace(Stop.Address) ? Stop.Address : "Location pending";

    protected override void OnParametersSet()
    {
        if (_referenceNotes != Stop.Notes || _referenceJob != Stop.Job)
        {
            _referenceNotes = Stop.Notes;
            _referenceJob = Stop.Job;
            AppointmentReferences = StopAppointmentReference.Extract(_referenceNotes, _referenceJob);
        }
        PlannedStop = new(Stop.Id, Stop.Name, string.Join(", ", new[] { Stop.Address, Stop.City, Stop.Province, Stop.ZipCode, Stop.Country }
            .Where(part => !string.IsNullOrWhiteSpace(part))), Stop.Sequence, new((double)(Stop.Latitude ?? 0), (double)(Stop.Longitude ?? 0)))
        {
            Job = Job, ScheduledDate = Stop.ScheduledDate ?? FallbackDate, ScheduledTime = Stop.ScheduledTime,
            ScheduledDate2 = Stop.ScheduledDate2, ScheduledTime2 = Stop.ScheduledTime2
        };
        var match = DispatchId != Guid.Empty && Stop.Id != Guid.Empty
            ? Eta?.Stops.FirstOrDefault(estimate => estimate.DispatchId == DispatchId && estimate.StopId == Stop.Id)
            : null;
        StopEstimate = Completed || Eta is null ? null : Eta with { Stops = match is null ? [] : [match] };
        Memory.Update(DispatchId, PlannedStop, StopEstimate, Completed);
    }
}
