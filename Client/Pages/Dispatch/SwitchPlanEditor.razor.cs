using System.Globalization;
using System.Net;
using Client.Models.DTO;
using Client.Models.DTO.Dispatch;
using Client.Models.DTO.Execution;
using Client.Services;
using Microsoft.AspNetCore.Components;

namespace Client.Pages.Dispatch;

public partial class SwitchPlanEditor : IDisposable
{
  [Inject]
  private ApiService Api { get; set; } = default!;

  [Parameter]
  public Guid DispatchId { get; set; }

  [Parameter, EditorRequired]
  public SwitchWorkspace Workspace { get; set; } = default!;

  [Parameter]
  public Guid? AfterStopId { get; set; }

  [Parameter]
  public string? TransferKind { get; set; }

  [Parameter]
  public EventCallback<SwitchResult> Saved { get; set; }

  [Parameter]
  public EventCallback Cancelled { get; set; }

  [Parameter]
  public EventCallback<bool> BusyChanged { get; set; }

  private readonly CancellationTokenSource _lifetime = new();
  private readonly Guid _key = Guid.NewGuid();
  private SwitchWorkspace _workspace = default!;
  private List<SwitchLoadDraft> _loads = [];
  private List<DispatchResponse> _matches = [];
  private PlanSwitchRequest? _snapshot;
  private SwitchPreview? _preview;
  private Guid? _siteVisit;
  private Guid? _second;
  private string _site = "";
  private string _search = "";
  private string? _error;
  private bool _busy;
  private bool _uncertain;
  private bool _completed;
  private bool _disposed;
  private string Id => $"switch-plan-{DispatchId}";
  private IEnumerable<SwitchVisitOption> Sites =>
    _workspace
      .Loads.SelectMany(x => x.Visits)
      .DistinctBy(x => x.Id)
      .Where(x =>
        x.Latitude is >= -90 and <= 90 && x.Longitude is >= -180 and <= 180
      );

  protected override void OnInitialized()
  {
    _workspace = Workspace;
    _loads = Workspace.Loads.Select(SwitchLoadDraft.From).ToList();
    var current = _loads.SingleOrDefault(x =>
      x.Source.DispatchId == DispatchId
    );
    if (
      AfterStopId is { } id
      && current?.Source.Visits.Any(visit => visit.Id == id) == true
    )
    {
      current.Boundary = "after";
      current.SplitId = id;
      if (TransferKind is "drop_hook" or "resource_handoff")
        current.TransferKind = TransferKind;
    }
  }

  private void SelectSite()
  {
    var site = Sites.FirstOrDefault(x => x.Id == _siteVisit);
    _site =
      site is null ? ""
      : string.IsNullOrWhiteSpace(site.Address) ? site.Name
      : site.Address;
  }

  private async Task SearchAsync()
  {
    if (_busy || _snapshot is not null)
      return;
    _error = null;
    _matches.Clear();
    if (!int.TryParse(_search.Trim(), out var number) || number < 1)
    {
      _error = "Enter the other load's number, without its prefix.";
      return;
    }
    await BusyAsync(true);
    var result = await Api.GetAsync<PaginatedListDTO<DispatchResponse>>(
      $"api/dispatch?page=1&pageSize=10&search={number}",
      _lifetime.Token
    );
    if (_disposed)
      return;
    await BusyAsync(false);
    if (!result.Success || result.Response is null)
      _error = result.ErrorMessage;
    else
    {
      _matches = result
        .Response.Items.Where(x =>
          x.Id != DispatchId
          && x.LoadNumber == number
          && x.Status is not ("completed" or "cancelled" or "canceled")
        )
        .ToList();
      if (_matches.Count == 0)
        _error = "No eligible matching load was found.";
    }
  }

  private async Task SelectSecondAsync(Guid? id)
  {
    if (_busy || _snapshot is not null)
      return;
    await BusyAsync(true);
    _error = null;
    var suffix = id.HasValue ? $"?secondDispatchId={id}" : "";
    var result = await Api.GetAsync<SwitchWorkspace>(
      $"api/dispatch/{DispatchId}/execution/switch-workspace{suffix}",
      _lifetime.Token
    );
    if (_disposed)
      return;
    await BusyAsync(false);
    if (!result.Success || result.Response is null)
    {
      _error = result.ErrorMessage;
      return;
    }
    var next = result.Response;
    var expected = id is { } second
      ? new[] { DispatchId, second }
      : new[] { DispatchId };
    if (
      next.Loads.Count != expected.Length
      || !next.Loads.Select(x => x.DispatchId).ToHashSet().SetEquals(expected)
    )
    {
      _error = "The returned Switch workspace does not match selected loads.";
      return;
    }
    _loads = next
      .Loads.Select(load =>
        _loads.FirstOrDefault(draft =>
          draft.Source.DispatchId == load.DispatchId
          && draft.Source.SourceSignature == load.SourceSignature
          && draft.Source.OutgoingLegId == load.OutgoingLegId
          && draft.Source.ExpectedOutgoingRevision
            == load.ExpectedOutgoingRevision
        ) ?? SwitchLoadDraft.From(load)
      )
      .ToList();
    _workspace = next;
    _second = id;
    _matches.Clear();
    if (!Sites.Any(x => x.Id == _siteVisit))
    {
      _siteVisit = null;
      _site = "";
    }
  }

  private async Task PreviewAsync()
  {
    if (_busy || _snapshot is not null)
      return;
    _error = null;
    _preview = null;
    var site = Sites.FirstOrDefault(x => x.Id == _siteVisit);
    if (site is null || string.IsNullOrWhiteSpace(_site) || _site.Length > 500)
    {
      _error = "Choose the exact transfer location and its name or address.";
      return;
    }
    var changes = new List<SwitchLoadChange>();
    foreach (var draft in _loads)
    {
      var change = draft.Request(out var error);
      if (change is null)
      {
        _error = $"Load {draft.Source.LoadNumber}: {error}";
        return;
      }
      changes.Add(change);
    }
    var request = new PlanSwitchRequest(_key, _site.Trim(), null, changes)
    {
      Latitude = site.Latitude,
      Longitude = site.Longitude,
      ConfirmCompleted = _completed,
    };
    await BusyAsync(true);
    var result = await Api.PostAsync<PlanSwitchRequest, SwitchPreview>(
      "api/execution/switches/preview",
      request,
      _lifetime.Token
    );
    if (_disposed)
      return;
    await BusyAsync(false);
    if (!result.Success || result.Response is null)
      _error = result.ErrorMessage;
    else
    {
      _preview = result.Response;
      if (
        _preview.CanPlan
        && (
          _preview.Loads.Count != request.Loads.Count
          || !_preview
            .Loads.Select(x => x.DispatchId)
            .ToHashSet()
            .SetEquals(request.Loads.Select(x => x.DispatchId))
        )
      )
      {
        _preview = null;
        _error = "The preview does not match the selected loads. Retry it.";
        return;
      }
      if (_preview.CanPlan)
        _snapshot = request;
    }
  }

  private async Task SaveAsync()
  {
    if (_busy || _disposed || _snapshot is null || _preview?.CanPlan != true)
      return;
    await BusyAsync(true);
    _error = null;
    var result = await Api.PostAsync<PlanSwitchRequest, SwitchResult>(
      "api/execution/switches",
      _snapshot,
      _lifetime.Token
    );
    if (_disposed)
      return;
    await BusyAsync(false);
    if (!result.Success || result.Response is null)
    {
      _error = result.ErrorMessage;
      _uncertain =
        result.HttpStatusCode
          is not (
            HttpStatusCode.BadRequest
            or HttpStatusCode.Conflict
            or HttpStatusCode.Forbidden
            or HttpStatusCode.Unauthorized
          );
      return;
    }
    await Saved.InvokeAsync(result.Response);
  }

  private void EditAgain()
  {
    if (_busy || _uncertain)
      return;
    _snapshot = null;
    _preview = null;
    _error = null;
  }

  private async Task CancelAsync()
  {
    if (!_busy)
      await Cancelled.InvokeAsync();
  }

  private async Task BusyAsync(bool value)
  {
    _busy = value;
    await BusyChanged.InvokeAsync(value);
  }

  private string Resource(
    Guid? id,
    IReadOnlyList<SwitchResourceOption> values
  ) =>
    id is null
      ? "Not recorded"
      : values.FirstOrDefault(x => x.Id == id)?.Name ?? "Unavailable resource";

  private string Assignment(ExecutionAssignment value) =>
    $"Truck {Resource(value.TruckId, _workspace.Trucks)} · "
    + $"Driver {Resource(value.DriverId, _workspace.Drivers)} · "
    + $"Trailer {Resource(value.TrailerId, _workspace.Trailers)}"
    + (
      value.CoDriverId.HasValue
        ? $" · Co-driver {Resource(value.CoDriverId, _workspace.Drivers)}"
        : ""
    );

  private int LoadNumber(Guid id) =>
    _workspace.Loads.First(x => x.DispatchId == id).LoadNumber;

  private static string PlannedTime(DateTimeOffset? value) =>
    value
      ?.ToLocalTime()
      .ToString("MMM d · hh:mm tt", CultureInfo.InvariantCulture)
    ?? "Not planned";

  public void Dispose()
  {
    _disposed = true;
    _lifetime.Cancel();
    _lifetime.Dispose();
  }
}
