using System.Globalization;
using Client.Models.DTO.Planning;
using Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Client.Shared.Fuel.FuelPlanEditor;

public sealed record FuelEditorStation(
  Guid StationId,
  string Name,
  Guid? BeforeStopId,
  bool AddNew,
  long Sequence
);

public partial class FuelPlanEditor : IAsyncDisposable
{
  [Inject]
  private ApiService Api { get; set; } = default!;

  [Inject]
  private TimeProvider Clock { get; set; } = default!;

  [Inject]
  private IJSRuntime JS { get; set; } = default!;

  [Inject]
  private ILogger<FuelPlanEditor> Logger { get; set; } = default!;

  [Parameter, EditorRequired]
  public Guid TruckId { get; set; }

  [Parameter, EditorRequired]
  public Guid DispatchId { get; set; }

  [Parameter]
  public Guid? ExecutionLegId { get; set; }

  [Parameter]
  public long AssignmentRevision { get; set; }

  [Parameter]
  public string TruckNumber { get; set; } = "";

  [Parameter]
  public DateTime? InitialCalculatedAt { get; set; }

  [Parameter]
  public FuelEditorStation? Station { get; set; }

  [Parameter]
  public EventCallback<FuelPlanEditPreview> Saved { get; set; }

  [Parameter]
  public EventCallback<AutomaticPlanningResult> Reset { get; set; }

  [Parameter]
  public EventCallback<bool> BusyChanged { get; set; }

  [Parameter]
  public EventCallback Failed { get; set; }

  [Parameter]
  public EventCallback Closed { get; set; }

  [Parameter]
  public EventCallback<FuelPlanStop?> StationSelected { get; set; }
  private (Guid Truck, Guid Dispatch, Guid? Leg, long Revision) _identity;
  private ElementReference _surface;
  private ElementReference _stopList;
  private Task? _reorderInitialization;
  private IJSObjectReference? _reorderModule;
  private IJSObjectReference? _reorder;
  private DotNetObjectReference<FuelPlanEditor>? _callbackReference;
  private Guid? _publishedSelection;
  private string _orderAnnouncement = "";
  private long _stationSequence = -1;
  private readonly List<DraftStop> _stops = [];
  private CancellationTokenSource? _request;
  private long _generation;
  private DateTime? _expectedCalculatedAt;
  private FuelPlanEditPreview? _preview;
  private FuelQuantityChoices? _quantityChoices;
  private List<string> _errors = [];
  private int _selected;
  private bool _loading;
  private bool _previewing;
  private bool _saving;
  private bool _dirty;
  private bool _valid;
  private bool _valuesCurrent;

  private enum MobileView
  {
    Fuel,
    Route,
    Map,
  }

  private MobileView _mobileView = MobileView.Fuel;
  private bool _disposed;
  private bool _interopDisposed;
  private string Endpoint => $"api/dispatch/{DispatchId}/planning/fuel";
  private DraftStop? Selected =>
    _selected >= 0 && _selected < _stops.Count ? _stops[_selected] : null;
  private FuelPlanStop? SelectedResult =>
    _valuesCurrent && Selected is { } selected ? ResultFor(selected) : null;
  private FuelPlanStop? SelectedDetails =>
    Selected is { } selected ? ResultFor(selected) : null;
  private double SliderMaximum =>
    _quantityChoices is { } choices
    && choices.StopIndex == _selected
    && choices.Options.Count > 0
      ? choices.Options[^1].SliderGallons
      : Selected?.SliderMaximum ?? 25;
  private bool QuantityChoicesReady =>
    _preview?.QuantityChoices is null
    || (
      !_previewing
      && _quantityChoices is { Options.Count: > 0 } choices
      && choices.StopIndex == _selected
    );
  private bool CanEditQuantity =>
    QuantityChoicesReady
    && Selected?.Edit.PurchaseLimitGallons is { } limit
    && double.IsFinite(limit)
    && limit > 0;
  private bool CanBuyPartial => CanEditQuantity && SliderMaximum > 25;
  private bool Busy => _loading || _saving;
  private bool CanSave =>
    !Busy && !_previewing && _dirty && _valid && _errors.Count == 0;

  private sealed class DraftStop(FuelPlanEditStop edit, string name)
  {
    public Guid Key { get; } = Guid.NewGuid();
    public FuelPlanEditStop Edit { get; set; } = edit;
    public string Name { get; set; } = name;
    public double SliderGallons { get; set; } =
      edit.FillToTarget ? 25 : edit.BuyGallons;
    public double SliderMaximum { get; set; } =
      RangeMaximum(edit.PurchaseLimitGallons);
  }

  private static double RangeMaximum(double? purchaseLimit) =>
    purchaseLimit is { } limit && double.IsFinite(limit) && limit >= 0
      ? Math.Max(25, Math.Ceiling(limit / 5) * 5)
      : 25;

  private sealed record TimelineEntry(
    string Key,
    DraftStop? Fuel = null,
    PlanStop? Stop = null,
    bool Last = false
  );

  private List<FuelPlanEditSegment> Segments => _preview?.Segments ?? [];

  private List<TimelineEntry> Timeline()
  {
    var entries = new List<TimelineEntry>();
    var segments = Segments;
    var nextAnchor = 0;
    foreach (var row in _stops)
    {
      var segment = segments.FindIndex(item =>
        item.BeforeStopId == row.Edit.BeforeStopId
      );
      while (nextAnchor < segment)
      {
        var anchor = segments[nextAnchor++];
        entries.Add(
          new($"stop:{anchor.BeforeStopId}", Stop: anchor.BeforeStop)
        );
      }
      entries.Add(new(row.Key.ToString(), row));
    }
    while (nextAnchor < segments.Count)
    {
      var anchor = segments[nextAnchor++];
      entries.Add(
        new(
          $"stop:{anchor.BeforeStopId}",
          Stop: anchor.BeforeStop,
          Last: nextAnchor == segments.Count
        )
      );
    }
    return entries;
  }

  protected override async Task OnAfterRenderAsync(bool firstRender)
  {
    if (_disposed)
      return;
    try
    {
      if (firstRender)
        await _surface.FocusAsync(preventScroll: true);
      if (!_loading && _preview is not null && _reorderInitialization is null)
        await (_reorderInitialization = InitializeReorderAsync());
    }
    catch (JSDisconnectedException) { }
  }

  private async Task InitializeReorderAsync()
  {
    _reorderModule = await JS.InvokeAsync<IJSObjectReference>(
      "import",
      "./js/generated/shared/reorderList.js"
    );
    if (_disposed)
      return;
    _callbackReference = DotNetObjectReference.Create(this);
    _reorder = await _reorderModule.InvokeAsync<IJSObjectReference>(
      "attachReorderList",
      _stopList,
      _callbackReference
    );
  }

  protected override async Task OnParametersSetAsync()
  {
    if (_disposed)
      return;
    if (_identity != (TruckId, DispatchId, ExecutionLegId, AssignmentRevision))
    {
      _identity = (TruckId, DispatchId, ExecutionLegId, AssignmentRevision);
      _stationSequence = -1;
      _stops.Clear();
      _preview = null;
      _quantityChoices = null;
      _expectedCalculatedAt = InitialCalculatedAt;
      _publishedSelection = null;
      _orderAnnouncement = "";
      _mobileView = MobileView.Fuel;
      _dirty = _valid = _valuesCurrent = _saving = _previewing = false;
      await LoadAsync();
    }
    if (
      !_loading
      && !_disposed
      && Station is { } station
      && station.Sequence != _stationSequence
    )
    {
      _stationSequence = station.Sequence;
      await SelectStationAsync(station);
    }
    await PublishSelectionAsync();
  }

  private async Task LoadAsync()
  {
    var request = BeginRequest();
    var generation = _generation;
    _loading = true;
    _errors = [];
    try
    {
      var response = await Api.PostAsync<
        FuelPlanEditRequest,
        FuelPlanEditPreview
      >($"{Endpoint}/edit/preview", EditRequest(null, null, 0), request.Token);
      if (!Owns(request, generation))
        return;
      if (!response.Success || response.Response is not { } preview)
      {
        _errors = Errors(response.Errors);
        return;
      }
      if (!MatchesScope(preview.Plan))
      {
        _errors = ["The truck assignment changed. Reopen the fuel plan."];
        return;
      }
      // This token belongs to the opened draft, not subsequent polling or
      // preview results.
      _expectedCalculatedAt = preview.ExpectedCalculatedAt;
      _preview = preview;
      _quantityChoices = preview.QuantityChoices;
      _stops.Clear();
      foreach (var edit in preview.Stops)
        _stops.Add(new(edit, NameFor(edit, preview.Plan)));
      _errors = preview.Errors;
      _valuesCurrent = preview.ValuesAvailable;
      _valid = _errors.Count == 0 && preview.ValuesAvailable;
      _selected = 0;
    }
    catch (OperationCanceledException) when (request.IsCancellationRequested)
    { }
    finally
    {
      if (Owns(request, generation))
      {
        _loading = false;
        _request = null;
      }
      request.Dispose();
    }
  }

  private Task SelectStationAsync(FuelEditorStation station)
  {
    if (_preview is null || Busy)
      return Task.CompletedTask;
    var index = station.AddNew
      ? -1
      : _stops.FindIndex(stop =>
        stop.Edit.StationId == station.StationId
        && (
          station.BeforeStopId is null
          || stop.Edit.BeforeStopId == station.BeforeStopId
        )
      );
    if (index >= 0)
      return SelectRowAsync(index);
    var segments = Segments;
    var preferred = station.BeforeStopId ?? Selected?.Edit.BeforeStopId;
    var before = segments.Any(segment => segment.BeforeStopId == preferred)
      ? preferred
      : segments.FirstOrDefault()?.BeforeStopId;
    var segmentIndex = segments.FindIndex(segment =>
      segment.BeforeStopId == before
    );
    var insertion = _stops.FindIndex(row =>
      segments.FindIndex(segment =>
        segment.BeforeStopId == row.Edit.BeforeStopId
      ) > segmentIndex
    );
    if (insertion < 0)
      insertion = _stops.Count;
    _stops.Insert(
      insertion,
      new(new(station.StationId, before, 0, true), station.Name)
    );
    _selected = insertion;
    _mobileView = MobileView.Fuel;
    return ChangedAsync();
  }

  private async Task SelectRowAsync(int index)
  {
    if (_disposed || Busy || index < 0 || index >= _stops.Count)
      return;
    _selected = index;
    _mobileView = MobileView.Fuel;
    await PublishSelectionAsync();
    if (
      _preview?.QuantityChoices is not null
      && _quantityChoices?.StopIndex != index
    )
      await ChangedAsync(refreshOnly: true);
  }

  [JSInvokable]
  public async Task OnFuelStopMoved(
    string sourceKey,
    string targetKey,
    bool after
  )
  {
    if (_disposed || Busy)
      return;
    var entries = Timeline();
    var from = entries.FindIndex(row =>
      row.Key == sourceKey && row.Fuel is not null
    );
    var to = entries.FindIndex(row => row.Key == targetKey);
    if (from < 0 || to < 0 || from == to || (after && entries[to].Last))
      return;
    var destination = to + (after ? 1 : 0) - (from < to ? 1 : 0);
    await MoveRowAsync(entries, from, destination);
    if (!_disposed)
      await InvokeAsync(StateHasChanged);
  }

  private Task MoveWithKeyboardAsync(Guid key, KeyboardEventArgs args)
  {
    var entries = Timeline();
    var from = entries.FindIndex(row => row.Fuel?.Key == key);
    var direction =
      args.Key is "ArrowUp" or "ArrowLeft" ? -1
      : args.Key is "ArrowDown" or "ArrowRight" ? 1
      : 0;
    return direction == 0
      ? Task.CompletedTask
      : MoveRowAsync(entries, from, from + direction);
  }

  private async Task MoveRowAsync(List<TimelineEntry> entries, int from, int to)
  {
    if (
      _disposed
      || Busy
      || from < 0
      || to < 0
      || from >= entries.Count
      || to >= entries.Count
      || from == to
      || entries[from].Fuel is not { } row
    )
      return;
    var entry = entries[from];
    entries.RemoveAt(from);
    entries.Insert(to, entry);
    var nextStop = entries
      .Skip(to + 1)
      .FirstOrDefault(item => item.Stop is not null)
      ?.Stop;
    if (Segments.Count > 0 && nextStop is null)
      return;
    row.Edit = row.Edit with { BeforeStopId = nextStop?.Id };
    _stops.Clear();
    _stops.AddRange(
      entries.Where(item => item.Fuel is not null).Select(item => item.Fuel!)
    );
    _selected = _stops.IndexOf(row);
    _orderAnnouncement =
      $"{row.Name}, fuel stop {_selected + 1}. {PositionFor(row)}";
    await ChangedAsync();
  }

  private Task QuantityAsync(ChangeEventArgs args)
  {
    if (
      Busy
      || !CanEditQuantity
      || Selected is not { } selected
      || !double.TryParse(
        args.Value?.ToString(),
        NumberStyles.Float,
        CultureInfo.InvariantCulture,
        out var gallons
      )
      || !double.IsFinite(gallons)
      || gallons < 25
      || gallons > SliderMaximum
      || gallons % 5 != 0
    )
      return Task.CompletedTask;
    if (ApplyQuantityChoice(gallons))
      return Task.CompletedTask;
    var full = gallons == SliderMaximum;
    if (!full)
      selected.SliderGallons = gallons;
    selected.Edit = selected.Edit with
    {
      BuyGallons = full ? 0 : gallons,
      FillToTarget = full,
    };
    return ChangedAsync(debounce: true);
  }

  private Task FullTankAsync(ChangeEventArgs args)
  {
    if (Busy || !CanEditQuantity || Selected is not { } selected)
      return Task.CompletedTask;
    var full = args.Value is true;
    if (!full && !CanBuyPartial)
      return Task.CompletedTask;
    var quantity = Math.Clamp(
      Math.Round(selected.SliderGallons / 5) * 5,
      25,
      Math.Max(25, SliderMaximum - 5)
    );
    if (ApplyQuantityChoice(full ? SliderMaximum : quantity))
      return Task.CompletedTask;
    selected.Edit = selected.Edit with
    {
      FillToTarget = full,
      BuyGallons = full ? 0 : quantity,
    };
    selected.SliderGallons = quantity;
    return ChangedAsync();
  }

  private Task RemoveAsync()
  {
    if (Busy || Selected is null)
      return Task.CompletedTask;
    _stops.RemoveAt(_selected);
    _selected = Math.Max(0, Math.Min(_selected, _stops.Count - 1));
    return ChangedAsync();
  }

  private bool ApplyQuantityChoice(double gallons)
  {
    if (
      _quantityChoices is not { } choices
      || choices.StopIndex != _selected
      || _preview is null
      || choices.Options.FirstOrDefault(option =>
        option.SliderGallons == gallons
      )
        is not { } choice
      || choice.Visits.Count != _stops.Count
      || _preview.Plan.Stops.Count != _stops.Count
    )
      return false;
    for (var i = 0; i < _stops.Count; i++)
    {
      var visit = choice.Visits[i];
      var row = _stops[i];
      row.Edit = row.Edit with
      {
        BuyGallons = visit.BuyGallons,
        FillToTarget = visit.FillToTarget,
        PurchaseLimitGallons = visit.PurchaseLimitGallons,
      };
      row.SliderGallons = visit.BuyGallons;
      row.SliderMaximum = RangeMaximum(visit.PurchaseLimitGallons);
      var stop = _preview.Plan.Stops[i];
      stop.ArrivalGallons = visit.ArrivalGallons;
      stop.BuyGallons = visit.BuyGallons;
      stop.DepartureGallons = visit.DepartureGallons;
      stop.FillToTarget = visit.FillToTarget;
      stop.PurchaseCostUsd = visit.PurchaseCostUsd;
    }
    _preview.Plan.ArrivalGallons = choice.ArrivalGallons;
    _preview.Plan.PurchaseGallons = choice.PurchaseGallons;
    _preview.Plan.PurchaseCostUsd = choice.PurchaseCostUsd;
    _preview.Plan.EconomicCostUsd = choice.EconomicCostUsd;
    _preview.Plan.ExpectedFutureFuelCostUsd = choice.ExpectedFutureFuelCostUsd;
    _preview.Plan.ManuallyEdited = true;
    _preview.Plan.NeedsRefresh = choice.Errors.Count > 0;
    _preview.Plan.RefreshReasons = choice.Errors;
    _errors = choice.Errors;
    _dirty = _valuesCurrent = true;
    _valid = _errors.Count == 0;
    return true;
  }

  private async Task ChangedAsync(
    bool debounce = false,
    bool refreshOnly = false
  )
  {
    if (!refreshOnly)
      _dirty = true;
    _quantityChoices = null;
    _valid = false;
    _valuesCurrent = false;
    _errors = [];
    _previewing = true;
    var request = BeginRequest();
    var generation = _generation;
    var edits = _stops.Select(stop => stop.Edit).ToList();
    try
    {
      if (debounce)
        await Task.Delay(TimeSpan.FromMilliseconds(250), Clock, request.Token);
      if (!Owns(request, generation))
        return;
      var response = await Api.PostAsync<
        FuelPlanEditRequest,
        FuelPlanEditPreview
      >(
        $"{Endpoint}/edit/preview",
        EditRequest(_expectedCalculatedAt, edits, _selected),
        request.Token
      );
      if (!Owns(request, generation))
        return;
      if (!response.Success || response.Response is not { } preview)
      {
        _errors = Errors(response.Errors);
        return;
      }
      ApplyPreview(preview);
      await PublishSelectionAsync();
    }
    catch (OperationCanceledException) when (request.IsCancellationRequested)
    { }
    finally
    {
      if (Owns(request, generation))
      {
        _previewing = false;
        _request = null;
      }
      request.Dispose();
    }
  }

  private void ApplyPreview(FuelPlanEditPreview preview)
  {
    if (!MatchesScope(preview.Plan))
    {
      _valid = _valuesCurrent = false;
      _errors = ["The truck assignment changed. Reopen the fuel plan."];
      return;
    }
    _preview = preview;
    _quantityChoices = preview.QuantityChoices;
    _errors = preview.Errors;
    var matching =
      preview.Stops.Count == _stops.Count
      && !preview
        .Stops.Where(
          (stop, index) => stop.StationId != _stops[index].Edit.StationId
        )
        .Any();
    _valuesCurrent = matching && preview.ValuesAvailable;
    _valid = _valuesCurrent && _errors.Count == 0;
    if (!matching)
    {
      _quantityChoices = null;
      foreach (var row in _stops)
        row.Edit = row.Edit with { PurchaseLimitGallons = null };
      _errors = ["The stop order changed. Review the plan and try again."];
      return;
    }
    for (var index = 0; index < _stops.Count; index++)
    {
      var row = _stops[index];
      var canonical = preview.Stops[index];
      row.Edit = canonical;
      row.SliderGallons = canonical.BuyGallons;
      if (
        canonical.PurchaseLimitGallons is { } limit
        && double.IsFinite(limit)
        && limit >= 0
      )
        row.SliderMaximum = RangeMaximum(limit);
      row.Name = NameFor(canonical, preview.Plan, row.Name);
    }
  }

  private async Task SaveAsync()
  {
    if (!CanSave)
      return;
    var request = BeginRequest();
    var generation = _generation;
    _saving = true;
    try
    {
      var response = await Api.PutAsync<
        FuelPlanEditRequest,
        FuelPlanEditPreview
      >(
        $"{Endpoint}/edit",
        EditRequest(
          _expectedCalculatedAt,
          _stops.Select(stop => stop.Edit).ToList()
        ),
        request.Token
      );
      if (!Owns(request, generation))
        return;
      if (!response.Success || response.Response is not { } preview)
      {
        _errors = Errors(response.Errors);
        return;
      }
      ApplyPreview(preview);
      if (!_valid)
        return;
      _dirty = false;
      await Saved.InvokeAsync(preview);
    }
    catch (OperationCanceledException) when (request.IsCancellationRequested)
    { }
    finally
    {
      if (Owns(request, generation))
      {
        _saving = false;
        _request = null;
      }
      request.Dispose();
    }
  }

  private async Task ResetAsync()
  {
    if (_disposed || Busy)
      return;
    var request = BeginRequest();
    var generation = _generation;
    _saving = true;
    _mobileView = MobileView.Fuel;
    try
    {
      await BusyChanged.InvokeAsync(true);
      if (!Owns(request, generation))
        return;
      var response = await Api.PostAsync<
        FuelPlanResetRequest,
        AutomaticPlanningResult
      >(
        $"{Endpoint}/reset",
        new(_expectedCalculatedAt)
        {
          ExecutionLegId = ExecutionLegId,
          AssignmentRevision = ExecutionLegId.HasValue
            ? AssignmentRevision
            : null,
        },
        request.Token
      );
      if (!Owns(request, generation))
        return;
      if (!response.Success || response.Response is not { } result)
      {
        Logger.LogWarning(
          "Calculate Fuel failed for dispatch {DispatchId}: {Reason}",
          DispatchId,
          string.Join("; ", Errors(response.Errors))
        );
        _errors = Errors(response.Errors);
        await Failed.InvokeAsync();
        return;
      }
      await Reset.InvokeAsync(result);
    }
    catch (OperationCanceledException) when (request.IsCancellationRequested)
    { }
    finally
    {
      if (Owns(request, generation))
      {
        _saving = false;
        _request = null;
        await BusyChanged.InvokeAsync(false);
      }
      request.Dispose();
    }
  }

  private Task CloseAsync()
  {
    if (_saving)
      return Task.CompletedTask;
    _request?.Cancel();
    _generation++;
    return Closed.InvokeAsync();
  }

  private Task OnKeyDown(KeyboardEventArgs args) =>
    args.Key == "Escape" ? CloseAsync() : Task.CompletedTask;

  private CancellationTokenSource BeginRequest()
  {
    _request?.Cancel();
    _generation++;
    return _request = new();
  }

  private FuelPlanEditRequest EditRequest(
    DateTime? calculatedAt,
    List<FuelPlanEditStop>? stops,
    int? quantityStopIndex = null
  ) =>
    new(calculatedAt, stops, quantityStopIndex)
    {
      ExecutionLegId = ExecutionLegId,
      AssignmentRevision = ExecutionLegId.HasValue ? AssignmentRevision : null,
    };

  private bool MatchesScope(FuelPlan plan) =>
    plan.TruckId == TruckId
    && plan.ExecutionLegId == ExecutionLegId
    && (
      !ExecutionLegId.HasValue || plan.AssignmentRevision == AssignmentRevision
    );

  private bool Owns(CancellationTokenSource request, long generation) =>
    !_disposed
    && !request.IsCancellationRequested
    && generation == _generation
    && ReferenceEquals(request, _request);

  private FuelPlanStop? ResultFor(DraftStop row) =>
    _preview?.Plan.Stops.FirstOrDefault(stop =>
      stop.StationId == row.Edit.StationId
      && stop.BeforeStopId == row.Edit.BeforeStopId
    );

  private double? CostFor(DraftStop row) =>
    _valuesCurrent ? ResultFor(row)?.PurchaseCostUsd : null;

  private string PositionFor(DraftStop row)
  {
    var segment = Segments.FirstOrDefault(item =>
      item.BeforeStopId == row.Edit.BeforeStopId
    );
    if (segment is null)
      return "Drag to choose a route position";
    return segment.AfterStop is { } after
      ? $"After {StopJob(after)} · {after.Name}"
      : $"Before {StopJob(segment.BeforeStop)} · {segment.BeforeStop.Name}";
  }

  private static bool IsPickup(PlanStop stop) =>
    stop.Job.Contains("pick", StringComparison.OrdinalIgnoreCase);

  private static string StopJob(PlanStop stop) =>
    IsPickup(stop) ? "Pickup" : "Delivery";

  private async Task PublishSelectionAsync()
  {
    if (_disposed)
      return;
    var row = Selected;
    var details = row is null ? null : ResultFor(row);
    if (row is not null && details is null)
      return;
    if (_publishedSelection == row?.Key)
      return;
    _publishedSelection = row?.Key;
    await StationSelected.InvokeAsync(details);
  }

  private static string NameFor(
    FuelPlanEditStop edit,
    FuelPlan plan,
    string fallback = "Fuel station"
  ) =>
    plan.Stops.FirstOrDefault(stop =>
      stop.StationId == edit.StationId
      && (edit.BeforeStopId is null || stop.BeforeStopId == edit.BeforeStopId)
    )?.Name ?? fallback;

  private static List<string> Errors(List<string>? errors) =>
    errors?.Where(text => !string.IsNullOrWhiteSpace(text)).ToList()
      is { Count: > 0 } messages
      ? messages
      : ["The plan could not be updated. Please try again."];

  private static string Quantity(double? value) =>
    value is { } number && double.IsFinite(number)
      ? number.ToString("N0", CultureInfo.InvariantCulture)
      : "—";

  private string SelectedPrice =>
    SelectedDetails is { YourPrice: > 0 } details
    && double.IsFinite(details.YourPrice)
      ? $"{details.YourPrice.ToString("N3", CultureInfo.InvariantCulture)} {details.Currency} / {details.Unit}"
      : "—";

  private static string Money(double? value) =>
    value is { } number && double.IsFinite(number)
      ? $"${number.ToString("N2", CultureInfo.InvariantCulture)}"
      : "—";

  public void Dispose()
  {
    _disposed = true;
    _request?.Cancel();
    _request = null;
    _stops.Clear();
    _preview = null;
    _quantityChoices = null;
  }

  public async ValueTask DisposeAsync()
  {
    if (_interopDisposed)
      return;
    _interopDisposed = true;
    Dispose();
    try
    {
      if (_reorderInitialization is not null)
        await _reorderInitialization;
      if (_reorder is not null)
      {
        await _reorder.InvokeVoidAsync("dispose");
        await _reorder.DisposeAsync();
      }
      if (_reorderModule is not null)
        await _reorderModule.DisposeAsync();
    }
    catch (JSDisconnectedException) { }
    catch (TaskCanceledException) { }
    finally
    {
      _callbackReference?.Dispose();
    }
  }
}
