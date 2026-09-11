using Client.Models.DTO.Dispatch;
using Client.Models.DTO.Planning;
using Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Client.Pages.FleetMap;

public partial class NextLoadDetailsCard
{
  [Inject] private TimeProvider Clock { get; set; } = default!;
  [Parameter, EditorRequired] public NextLoadRoute Route { get; set; } = default!;
  [Parameter] public PlanStop? Stop { get; set; }
  [Parameter] public int StopIndex { get; set; }
  [Parameter] public double? DistanceMiles { get; set; }
  [Parameter] public DispatchResponse? Details { get; set; }
  [Parameter] public DispatchEta? Eta { get; set; }
  [Parameter] public bool Loading { get; set; }
  [Parameter] public bool Refreshing { get; set; }
  [Parameter] public bool Embedded { get; set; }
  [Parameter] public FuelStopArrival? FuelArrival { get; set; }
  private FuelStopArrival? ArrivalFuel => !StopCompleted && FuelArrival is { } fuel && fuel.DispatchId == Route.Id && fuel.StopId == Stop?.Id
    && double.IsFinite(fuel.Gallons) && double.IsFinite(fuel.Percent) && fuel.Gallons >= 0 && fuel.Percent is >= 0 and <= 100
    ? fuel : null;
  [Parameter] public string? Error { get; set; }
  [Parameter] public string? CopyMessage { get; set; }
  [Parameter] public EventCallback<string> OnCopy { get; set; }
  [Parameter] public EventCallback OnClose { get; set; }
  private readonly ArrivalDisplayMemory _arrivalMemory = new();
  private bool StopCompleted => Stop is null || Details?.Id == Route.Id && Details.Stops.Any(stop => stop.Id == Stop.Id
    && (stop.DepartedAt ?? stop.DeliveredAt ?? stop.PickedUpAt).HasValue);
  private bool HasDisplayedEta => !StopCompleted
    && _arrivalMemory.Display(Eta, Clock.GetUtcNow().UtcDateTime, Refreshing)?.Stops.Count > 0;

  private string Appointment => StopAppointmentDisplay.Format(Stop);
  private StopAddressLines Address { get; set; } = new("", "");
  private string? _address;
  private string? _referenceNotes, _referenceJob;
  private IReadOnlyList<string> AppointmentReferences { get; set; } = [];
  private double? LegMiles => StopIndex == 0 ? Route.Deadhead?.Miles
    : StopIndex > 0 && StopIndex <= Route.Legs.Count ? Route.Legs[StopIndex - 1].Miles : null;

  protected override void OnParametersSet()
  {
    _arrivalMemory.Update(Route.Id, Stop, Eta, StopCompleted);
    if (_address != Stop?.Address)
    {
      _address = Stop?.Address;
      Address = StopAddressLines.Create(_address);
    }
    if (_referenceNotes == Stop?.Notes && _referenceJob == Stop?.Job) return;
    _referenceNotes = Stop?.Notes;
    _referenceJob = Stop?.Job;
    AppointmentReferences = StopAppointmentReference.Extract(_referenceNotes, _referenceJob);
  }

  private Task OnKeyDownAsync(KeyboardEventArgs args) => !Embedded && args.Key == "Escape" ? OnClose.InvokeAsync() : Task.CompletedTask;
  private static string Distance(double? miles) => miles is { } value && double.IsFinite(value)
    ? $"{value:N0} mi · {value * 1.609344:N0} km" : "—";
}
