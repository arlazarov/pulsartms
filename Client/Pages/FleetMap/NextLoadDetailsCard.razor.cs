using Client.Models.DTO.Dispatch;
using Client.Models.DTO.Planning;
using Client.Services;
using Client.Shared.Dispatch;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Client.Pages.FleetMap;

public partial class NextLoadDetailsCard
{
  [Inject]
  private TimeProvider Clock { get; set; } = default!;

  [Parameter, EditorRequired]
  public NextLoadRoute Route { get; set; } = default!;

  [Parameter]
  public PlanStop? Stop { get; set; }

  [Parameter]
  public int StopIndex { get; set; }

  [Parameter]
  public double? DistanceMiles { get; set; }

  [Parameter]
  public DispatchResponse? Details { get; set; }

  [Parameter]
  public DispatchEta? Eta { get; set; }

  [Parameter]
  public bool Loading { get; set; }

  [Parameter]
  public bool Refreshing { get; set; }

  [Parameter]
  public bool Embedded { get; set; }

  [Parameter]
  public bool Expanded { get; set; } = true;

  [Parameter]
  public FuelStopArrival? FuelArrival { get; set; }

  // The card says fuel as a named figure on its line, not as a dial.
  private string ArrivalFuelText =>
    ArrivalFuel is { } fuel
      ? $"{Math.Round(fuel.Percent)}% · {Math.Round(fuel.Gallons)} US gal"
      : "—";

  private FuelStopArrival? ArrivalFuel =>
    !StopCompleted
    && FuelArrival is { } fuel
    && fuel.DispatchId == Route.Id
    && fuel.StopId == Stop?.Id
    && double.IsFinite(fuel.Gallons)
    && double.IsFinite(fuel.Percent)
    && fuel.Gallons >= 0
    && fuel.Percent is >= 0 and <= 100
      ? fuel
      : null;

  [Parameter]
  public string? Error { get; set; }

  [Parameter]
  public string? CopyMessage { get; set; }

  [Parameter]
  public EventCallback<string> OnCopy { get; set; }

  [Parameter]
  public EventCallback OnClose { get; set; }
  private readonly ArrivalDisplayMemory _arrivalMemory = new();
  private bool StopCompleted =>
    Stop is null
    || Details?.Id == Route.Id
      && Details.Stops.Any(stop => stop.Id == Stop.Id && stop.IsCompleted);
  private bool HasDisplayedEta =>
    !StopCompleted
    && _arrivalMemory
      .Display(Eta, Clock.GetUtcNow().UtcDateTime, Refreshing)
      ?.Stops.Count > 0;

  private string Appointment => StopAppointmentDisplay.Format(Stop);
  private string StopPosition =>
    $"Load stop {StopIndex + 1} of "
    + Math.Max(Route.StopCount, Route.Stops.Count);
  private StopAddressLines Address { get; set; } = new("", "");
  private string? _address;
  private string? _referenceNotes,
    _referenceJob;
  private IReadOnlyList<string> AppointmentReferences { get; set; } = [];
  private string VisitLabel { get; set; } = "";
  private DispatchStopResponse? AssignmentStop =>
    Details?.Id == Route.Id && Stop is { Id: var id } && id != Guid.Empty
      ? Details.Stops.FirstOrDefault(value => value.Id == id)
      : null;
  private string? StopReference =>
    AssignmentStop is { DriverOnly: false } stop
    && (
      Stop?.Job.Contains("pick", StringComparison.OrdinalIgnoreCase) == true
      || Stop?.Job.Contains("drop", StringComparison.OrdinalIgnoreCase) == true
      || Stop?.Job.Contains("deliver", StringComparison.OrdinalIgnoreCase)
        == true
    )
    && !string.IsNullOrWhiteSpace(stop.StopNo)
      ? stop.StopNo.Trim()
      : null;
  private string StopReferenceLabel =>
    Stop?.Job.Contains("pick", StringComparison.OrdinalIgnoreCase) == true
      ? "PU #"
      : "DEL #";
  private string AssignmentTruck =>
    AssignmentStop is { DriverOnly: false } stop
      ? DispatchBoardRow.Text(stop.TruckNumber, Details?.TruckNumber)
      : "—";
  private string AssignmentTrailer =>
    Stop?.StateAfter != "Bobtail"
    && AssignmentStop is { DriverOnly: false, StateAfter: not "Bobtail" } stop
      ? DispatchBoardRow.Text(stop.TrailerNumber, Details?.TrailerNumber)
      : "—";
  private string AssignmentDriver =>
    AssignmentStop is { } stop
      ? DispatchBoardRow.Text(stop.DriverName, Details?.DriverName)
      : "—";
  private double? LegMiles =>
    StopIndex == 0 ? Route.Deadhead?.Miles
    : StopIndex > 0 && StopIndex <= Route.Legs.Count
      ? Route.Legs[StopIndex - 1].Miles
    : null;

  protected override void OnParametersSet()
  {
    VisitLabel =
      Details?.Id == Route.Id && Stop is not null
        ? DispatchStopPresentation
          .OrderedVisits(Details.Stops)
          .FirstOrDefault(visit => visit.Stop.Id == Stop.Id)
          ?.VisitLabel ?? ""
        : "";
    _arrivalMemory.Update(Route.Id, Stop, Eta, StopCompleted);
    if (_address != Stop?.Address)
    {
      _address = Stop?.Address;
      Address = StopAddressLines.Create(_address);
    }
    if (_referenceNotes == Stop?.Notes && _referenceJob == Stop?.Job)
      return;
    _referenceNotes = Stop?.Notes;
    _referenceJob = Stop?.Job;
    AppointmentReferences = StopAppointmentReference.Extract(
      _referenceNotes,
      _referenceJob
    );
  }

  private Task OnKeyDownAsync(KeyboardEventArgs args) =>
    !Embedded && args.Key == "Escape"
      ? OnClose.InvokeAsync()
      : Task.CompletedTask;
}
