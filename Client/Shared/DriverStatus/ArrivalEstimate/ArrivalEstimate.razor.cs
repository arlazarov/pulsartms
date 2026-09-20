using Client.Models.DTO.Planning;
using Client.Services;
using Microsoft.AspNetCore.Components;

namespace Client.Shared.DriverStatus.ArrivalEstimate;

public partial class ArrivalEstimate
{
  [Inject]
  private TimeProvider Clock { get; set; } = default!;

  [Parameter]
  public PlanStop? Stop { get; set; }
  private string Appointment => StopAppointmentDisplay.Format(Stop);

  [Parameter]
  public DispatchEta? Eta { get; set; }

  [Parameter]
  public bool ShowDutyStatus { get; set; } = true;

  [Parameter]
  public bool ShowAppointment { get; set; } = true;

  [Parameter]
  public bool ShowSummary { get; set; } = true;

  [Parameter]
  public bool ShowDetails { get; set; } = true;

  [Parameter]
  public bool ShowRecap { get; set; }

  [Parameter]
  public ArrivalDisplayMemory Memory { get; set; } = new();

  [Parameter]
  public Guid? DispatchId { get; set; }

  [Parameter]
  public bool Completed { get; set; }

  // Passed through to the forecast: which reading of it this place wants.
  [Parameter]
  public string Reading { get; set; } = string.Empty;

  [Parameter]
  public bool Refreshing { get; set; }
  private DispatchEta? DisplayEta =>
    Memory.Display(Eta, Clock.GetUtcNow().UtcDateTime, Refreshing);

  protected override void OnParametersSet()
  {
    Memory.Update(DispatchId, Stop, Eta, Completed);
  }
}
