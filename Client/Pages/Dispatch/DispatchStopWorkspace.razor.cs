using Client.Models.DTO;
using Client.Models.DTO.Dispatch;
using Client.Models.DTO.Dispatch.Workspace;
using Client.Shared.Dispatch;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Client.Pages.Dispatch;

public partial class DispatchStopWorkspace
{
  [Inject]
  private IJSRuntime JS { get; set; } = default!;

  private ElementReference _listElement;
  private Guid? _revealedSelection;
  private string _mobilePane = "details";
  private static readonly (string Key, string Label)[] MobilePanes =
  [
    ("stops", "Stops"),
    ("details", "Details"),
    ("notes", "Notes & files"),
    ("route", "Route"),
  ];

  private bool HasSupport =>
    SupportingContent is not null
    || RouteContent is not null
    || MapContent is not null;
  private IEnumerable<(string Key, string Label)> AvailablePanes =>
    MobilePanes.Where(pane =>
      pane.Key switch
      {
        "notes" => SupportingContent is not null,
        "route" => RouteContent is not null || MapContent is not null,
        _ => true,
      }
    );

  private void ShowMobilePane(string pane)
  {
    _mobilePane = pane;
    if (pane == "stops")
      _revealedSelection = null;
  }

  protected override async Task OnAfterRenderAsync(bool firstRender)
  {
    if (_selected is not { } selected || _revealedSelection == selected)
      return;
    _revealedSelection = selected;
    await using var module = await JS.InvokeAsync<IJSObjectReference>(
      "import",
      "./js/generated/dispatch/dispatch.js"
    );
    if (module is not null && _selected == selected)
      await module.InvokeVoidAsync(
        "revealStop",
        _listElement,
        selected.ToString()
      );
  }

  [Parameter, EditorRequired]
  public List<DispatchWorkspaceStop> Stops { get; set; } = [];

  [Parameter]
  public DispatchResponse? Load { get; set; }

  [Parameter]
  public bool DraftChanged { get; set; }

  [Parameter]
  public bool CanEdit { get; set; }

  [Parameter]
  public bool Disabled { get; set; }

  [Parameter]
  public bool AllowSelection { get; set; }

  [Parameter]
  public DispatchStopDrafts? StopDrafts { get; set; }

  [Parameter]
  public Guid? SelectedStopId { get; set; }

  [Parameter]
  public EventCallback<Guid?> SelectedStopIdChanged { get; set; }

  [Parameter]
  public EventCallback<Guid> StopActivated { get; set; }

  [Parameter]
  public EventCallback Changed { get; set; }

  [Parameter]
  public EventCallback<Guid> SwitchRequested { get; set; }

  [Parameter]
  public EventCallback<DispatchTransferStart> TransferRequested { get; set; }

  [Parameter]
  public EventCallback<Guid> VerifyAddressRequested { get; set; }

  [Parameter]
  public RenderFragment? SelectedStopActions { get; set; }

  [Parameter]
  public RenderFragment? RouteContent { get; set; }

  [Parameter]
  public RenderFragment? MapContent { get; set; }

  [Parameter]
  public RenderFragment? SupportingContent { get; set; }

  [Parameter]
  public RenderFragment? OperationsContent { get; set; }

  [Parameter]
  public Func<
    string,
    CancellationToken,
    Task<RequestResponseDTO<VerifiedDispatchAddress>>
  >? AddressLookup { get; set; }

  private readonly Dictionary<Guid, DispatchStopClockDraft> _clocks = [];
  private readonly string _detailsId = $"stop-details-{Guid.NewGuid():N}";
  private List<DispatchWorkspaceStop>? _source;
  private Guid? _requestedSelection;
  private Guid? _selected;
  private Guid? _dragged;
  private Guid? _dropTarget;
  private Guid? _removing;
  private bool _adding;
  private string _announcement = "";
  private string? _error;
  private bool Editable => CanEdit && !Disabled;

  private static string StopTone(DispatchWorkspaceStop stop) =>
    stop.Transfer is not null ? "is-transfer"
    : stop.Job is "Pick Up" or "Pickup" ? "is-pickup"
    : stop.Job == "Delivery" ? "is-delivery"
    : "";

  private static string StopIcon(DispatchWorkspaceStop stop) =>
    stop.Transfer is not null ? "route-overview"
    : stop.Job is "Pick Up" or "Pickup" ? "cargo"
    : "location";

  private DispatchWorkspaceStop? SelectedStop =>
    Stops.FirstOrDefault(stop => stop.Id == _selected);
  private DispatchWorkspaceStop? AddAnchor =>
    Stops.FirstOrDefault(stop => stop.Id == _selected && stop.CanMove);
  private bool CanAdd => Editable && AddAnchor is not null;
  private bool CanTransfer =>
    Editable
    && !DraftChanged
    && SelectedStop is { IsNew: false }
    && TransferRequested.HasDelegate;

  protected override void OnParametersSet()
  {
    var sourceChanged = !ReferenceEquals(_source, Stops);
    var selectionChanged = _requestedSelection != SelectedStopId;
    _requestedSelection = SelectedStopId;
    if (sourceChanged)
    {
      _source = Stops;
      _clocks.Clear();
      _error = null;
      _removing = null;
      _dragged = null;
      _dropTarget = null;
      _adding = false;
    }
    foreach (
      var id in _clocks.Keys.Where(id => !Stops.Any(s => s.Id == id)).ToArray()
    )
      _clocks.Remove(id);
    if (sourceChanged || selectionChanged)
    {
      _selected =
        SelectedStopId is { } selected && Stops.Any(s => s.Id == selected)
          ? selected
        : sourceChanged
          ? Stops.FirstOrDefault(stop => stop.CanEdit)?.Id
            ?? Stops.FirstOrDefault()?.Id
        : null;
    }
    else if (_selected is { } active && !Stops.Any(stop => stop.Id == active))
      _selected = null;
  }

  public bool Validate(out string? error)
  {
    foreach (var stop in Stops.Where(stop => stop.CanEdit))
    {
      if (!Clock(stop).Apply(stop, out error))
      {
        _mobilePane = "details";
        _selected = stop.Id;
        _error = $"Stop {stop.Sequence}: {error}";
        error = _error;
        return false;
      }
    }
    error = null;
    _error = null;
    return true;
  }

  private DispatchStopClockDraft Clock(DispatchWorkspaceStop stop)
  {
    if (!_clocks.TryGetValue(stop.Id, out var clock))
      _clocks[stop.Id] = clock = DispatchStopClockDraft.From(stop);
    return clock;
  }

  private async Task SelectAsync(Guid id)
  {
    if (Disabled && !AllowSelection)
      return;
    if (_dragged is { } moving)
    {
      await MoveAsync(moving, Stops.FindIndex(x => x.Id == id));
      return;
    }
    _mobilePane = "details";
    await StopActivated.InvokeAsync(id);
    if (_selected == id)
      return;
    _selected = id;
    _removing = null;
    _adding = false;
    await SelectedStopIdChanged.InvokeAsync(_selected);
  }

  private Task NotifyAsync() => Changed.InvokeAsync();

  private async Task MoveAsync(Guid id, int target)
  {
    if (!Editable || !DispatchStopDraftOrder.Move(Stops, id, target))
      return;
    _dragged = null;
    _dropTarget = null;
    _announcement = $"Stop moved to position {target + 1}. Not saved yet.";
    await Changed.InvokeAsync();
  }

  private bool CanMove(Guid id, int target) =>
    Editable && DispatchStopDraftOrder.CanMove(Stops, id, target);

  private void PickForMove(Guid id)
  {
    if (!Editable)
      return;
    _dragged = _dragged == id ? null : id;
    _dropTarget = null;
    _announcement = _dragged is null
      ? "Move cancelled."
      : "Select the destination stop.";
  }

  private async Task ReorderKeyAsync(Guid id, KeyboardEventArgs e)
  {
    if (e.Key == "Escape")
    {
      _dragged = null;
      _dropTarget = null;
      return;
    }
    var index = Stops.FindIndex(x => x.Id == id);
    if (e.Key is "ArrowUp" or "ArrowLeft")
      await MoveAsync(id, index - 1);
    if (e.Key is "ArrowDown" or "ArrowRight")
      await MoveAsync(id, index + 1);
  }

  private bool CanDrop(DispatchWorkspaceStop stop) =>
    _dragged is { } id && CanMove(id, Stops.IndexOf(stop));

  private void BeginDrag(Guid id)
  {
    if (!Editable)
      return;
    _dragged = id;
    _dropTarget = null;
  }

  private void EndDrag()
  {
    _dragged = null;
    _dropTarget = null;
  }

  private void PreviewDrop(DispatchWorkspaceStop stop)
  {
    var target = CanDrop(stop) ? stop.Id : (Guid?)null;
    if (_dropTarget == target)
      return;
    _dropTarget = target;
    if (target is not null)
      _announcement = $"Insert at position {Stops.IndexOf(stop) + 1}.";
  }

  private string DropPreviewClass(DispatchWorkspaceStop stop)
  {
    if (_dropTarget != stop.Id || !CanDrop(stop))
      return "";
    return Stops.FindIndex(x => x.Id == _dragged) < Stops.IndexOf(stop)
      ? "is-drop-after"
      : "is-drop-before";
  }

  private async Task DropAsync(DispatchWorkspaceStop stop)
  {
    if (_dragged is { } id)
      await MoveAsync(id, Stops.IndexOf(stop));
    _dragged = null;
  }

  private void ToggleAdd() => _adding = !_adding;

  private async Task TransferAsync(string kind)
  {
    if (!CanTransfer || _selected is not { } id)
      return;
    _adding = false;
    _mobilePane = "details";
    await TransferRequested.InvokeAsync(new(id, kind));
  }

  private async Task AddAsync(string job)
  {
    if (!CanAdd || AddAnchor is not { } anchor)
      return;
    var stop = new DispatchWorkspaceStop
    {
      Id = Guid.NewGuid(),
      IsNew = true,
      CanEdit = true,
      CanMove = true,
      CanRemove = true,
      SegmentKey = anchor.SegmentKey,
      ExecutionLegId = anchor.ExecutionLegId,
      Job = job,
      AppointmentMode = "unscheduled",
      TimeZoneId = anchor.TimeZoneId,
      Country = anchor.Country,
      TruckNumber = anchor.TruckNumber,
      TrailerNumber = anchor.TrailerNumber,
      DriverName = anchor.DriverName,
      CoDriverName = anchor.CoDriverName,
    };
    Stops.Insert(Stops.IndexOf(anchor) + 1, stop);
    DispatchStopDraftOrder.Renumber(Stops);
    await SelectAsync(stop.Id);
    _announcement = "New stop added to the draft. Enter its location.";
    await Changed.InvokeAsync();
  }

  private async Task RemoveAsync(DispatchWorkspaceStop stop)
  {
    if (!Editable || !stop.CanRemove || _removing != stop.Id)
      return;
    var index = Stops.IndexOf(stop);
    Stops.Remove(stop);
    _clocks.Remove(stop.Id);
    _removing = null;
    DispatchStopDraftOrder.Renumber(Stops);
    _selected = Stops.ElementAtOrDefault(Math.Min(index, Stops.Count - 1))?.Id;
    await SelectedStopIdChanged.InvokeAsync(_selected);
    _announcement = "Stop removed from the draft. Not saved yet.";
    await Changed.InvokeAsync();
  }

  private static string Location(DispatchWorkspaceStop stop) =>
    string.Join(
      ", ",
      new[] { stop.City, stop.Province }.Where(value =>
        !string.IsNullOrWhiteSpace(value)
      )
    );

  private static string Text(string? value) =>
    string.IsNullOrWhiteSpace(value) ? "—" : value;

  private DispatchStopResponse? RecordedStop(Guid id) =>
    Load?.Stops.FirstOrDefault(stop => stop.Id == id);
}

public sealed record DispatchTransferStart(Guid StopId, string Kind);
