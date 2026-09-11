using Microsoft.AspNetCore.Components;
using Client.Models.DTO.Planning;
using Client.Services;

namespace Client.Shared.DriverStatus.ArrivalEstimate;

public partial class ArrivalEstimate
{
    [Inject] private TimeProvider Clock { get; set; } = default!;
    [Parameter] public PlanStop? Stop { get; set; }
    private string Appointment => StopAppointmentDisplay.Format(Stop);
    [Parameter] public DispatchEta? Eta { get; set; }
    [Parameter] public bool ShowDutyStatus { get; set; } = true;
    [Parameter] public bool ShowAppointment { get; set; } = true;
    [Parameter] public bool ShowSummary { get; set; } = true;
    [Parameter] public bool ShowDetails { get; set; } = true;
    [Parameter] public bool ShowRecap { get; set; }
    [Parameter] public Client.Services.ArrivalDisplayMemory Memory { get; set; } = new();
    [Parameter] public Guid? DispatchId { get; set; }
    [Parameter] public bool Completed { get; set; }
    [Parameter] public bool Refreshing { get; set; }
    private DispatchEta? DisplayEta => Memory.Display(Eta, Clock.GetUtcNow().UtcDateTime, Refreshing);

    protected override void OnParametersSet()
    {
        Memory.Update(DispatchId, Stop, Eta, Completed);
    }
    private static string Duration(int minutes) => minutes >= 60 ? $"{minutes / 60}h {minutes % 60:00}m" : $"{minutes}m";
}
