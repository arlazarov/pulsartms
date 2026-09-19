using Client.Models;
using Client.Models.DTO.Planning;
using Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Client.Shared.Routing.RouteEditor;

public partial class RouteEditor
{
  [CascadingParameter]
  public DisplayUnits Units { get; set; } = DisplayUnits.Default;

  [Inject]
  private ApiService Api { get; set; } = default!;

  [Parameter]
  public Guid DispatchId { get; set; }

  [Parameter]
  public Guid? ExecutionLegId { get; set; }

  [Parameter]
  public Guid Session { get; set; }

  [Parameter]
  public string TruckNumber { get; set; } = "";

  [Parameter]
  public EventCallback<RouteEditorMap> MapChanged { get; set; }

  [Parameter]
  public EventCallback Saved { get; set; }

  [Parameter]
  public EventCallback Closed { get; set; }
  private readonly CancellationTokenSource _lifetime = new();
  private RouteChoicePreview? _preview;
  private List<RouteViaPoint> _via = [];
  private bool _editing,
    _loading,
    _saving,
    _valid,
    _changed,
    _addingPoint,
    _disposed;
  private string _address = "";
  private Guid _beforeStop;
  private string? _error;
  private int _selected = 1;
  private long _generation;
  private bool Busy => _loading || _saving;
  private string Endpoint => $"api/dispatch/{DispatchId}/planning/route";
  private RouteChoiceOption? Selected =>
    _preview?.Options.FirstOrDefault(o => o.Number == _selected);

  protected override Task OnInitializedAsync() => Calculate(true, true);

  private async Task Calculate(bool alternatives, bool useSaved = false)
  {
    if (Busy || _disposed)
      return;
    var generation = ++_generation;
    _loading = true;
    _valid = false;
    _error = null;
    var result = await Api.PostAsync<RouteChoiceRequest, RouteChoicePreview>(
      $"{Endpoint}/options",
      new([.. _via], alternatives, useSaved, ExecutionLegId),
      _lifetime.Token
    );
    if (_disposed || generation != _generation)
      return;
    _loading = false;
    if (
      !result.Success
      || result.Response is not { } preview
      || preview.DispatchId != DispatchId
    )
    {
      Console.Error.WriteLine(
        "Route preview failed for {0}: {1}",
        DispatchId,
        result.HttpStatusCode
      );
      _error =
        !result.Success && !string.IsNullOrWhiteSpace(result.ErrorMessage)
          ? result.ErrorMessage
          : "Could not calculate route options. Try again.";
      return;
    }
    _preview = preview;
    _via = [.. preview.ViaPoints];
    _selected = 1;
    _valid = true;
    _beforeStop = preview.Stops.Skip(1).Any(s => s.Id == _beforeStop)
      ? _beforeStop
      : preview.Stops.Last().Id;
    await Publish();
  }

  private Task Publish() =>
    _preview is null || _disposed
      ? Task.CompletedTask
      : MapChanged.InvokeAsync(
        new(
          Session,
          _preview with
          {
            ViaPoints = [.. _via],
          },
          _selected,
          _editing,
          _addingPoint
        )
      );

  private async Task SetEditing(bool editing)
  {
    _editing = editing;
    _addingPoint = false;
    await Publish();
  }

  public async Task Select(int option)
  {
    if (Busy || _preview?.Options.Any(o => o.Number == option) != true)
      return;
    _selected = option;
    _changed = true;
    await Publish();
    await InvokeAsync(StateHasChanged);
  }

  private async Task TogglePoint()
  {
    _addingPoint = !_addingPoint;
    await Publish();
  }

  private async Task AddCity()
  {
    if (Busy || _preview is null || _via.Count >= 20)
      return;
    _loading = true;
    _error = null;
    var label = _address.Trim();
    var result = await Api.PostAsync<object, RoutePoint>(
      $"{Endpoint}/via-location",
      new { Address = label },
      _lifetime.Token
    );
    if (_disposed)
      return;
    _loading = false;
    if (!result.Success || result.Response?.IsValid != true)
    {
      _error =
        "Location not found. Use a more specific address or add a point on the map.";
      return;
    }
    _via.Add(new(Guid.NewGuid(), _beforeStop, label, result.Response));
    _address = "";
    _changed = true;
    _valid = false;
    await Calculate(false);
  }

  public async Task PointChanged(
    string? id,
    int leg,
    double latitude,
    double longitude
  )
  {
    if (Busy || !_editing || _preview is null || _disposed)
      return;
    var point = new RoutePoint(latitude, longitude);
    if (!point.IsValid)
      return;
    var existing = Guid.TryParse(id, out var key)
      ? _via.FindIndex(v => v.Id == key)
      : -1;
    if (existing >= 0)
      _via[existing] = _via[existing] with
      {
        Point = point,
        Label = "Custom via point",
      };
    else
    {
      if (_via.Count >= 20)
        return;
      var before =
        leg >= 0 && leg + 1 < _preview.Stops.Count
          ? _preview.Stops[leg + 1].Id
          : _beforeStop;
      _via.Add(new(Guid.NewGuid(), before, "Custom via point", point));
    }
    _changed = true;
    _valid = false;
    _addingPoint = false;
    await Calculate(false);
    await InvokeAsync(StateHasChanged);
  }

  private async Task Remove(RouteViaPoint point)
  {
    _via.Remove(point);
    _changed = true;
    _valid = false;
    await Calculate(false);
  }

  private RouteViaPoint? Neighbor(RouteViaPoint point, int direction)
  {
    var leg = _via.Where(v => v.BeforeStopId == point.BeforeStopId).ToList();
    var index = leg.IndexOf(point) + direction;
    return index >= 0 && index < leg.Count ? leg[index] : null;
  }

  private async Task Move(RouteViaPoint point, int direction)
  {
    if (Busy || Neighbor(point, direction) is not { } neighbor)
      return;
    var index = _via.IndexOf(point);
    var other = _via.IndexOf(neighbor);
    (_via[index], _via[other]) = (_via[other], _via[index]);
    _changed = true;
    _valid = false;
    await Calculate(false);
  }

  private async Task Save()
  {
    if (Busy || !_valid || _preview is null || _disposed)
      return;
    _saving = true;
    _error = null;
    var result = await Api.PutAsync<RouteChoiceSave, long>(
      $"{Endpoint}/choice",
      new(_preview.Id, _selected, _preview.Revision, _preview.ExecutionLegId),
      _lifetime.Token
    );
    if (_disposed)
      return;
    _saving = false;
    if (!result.Success)
    {
      _valid = false;
      _error = "Could not save the route. Refresh the preview and try again.";
      Console.Error.WriteLine(
        "Route save failed for {0}: {1}",
        DispatchId,
        result.HttpStatusCode
      );
      return;
    }
    await Saved.InvokeAsync();
  }

  private Task Close() => _saving ? Task.CompletedTask : Closed.InvokeAsync();

  private Task KeyDown(KeyboardEventArgs args) =>
    args.Key == "Escape" ? Close() : Task.CompletedTask;

  private static string StopLabel(PlanStop stop) =>
    string.IsNullOrWhiteSpace(stop.Name) ? stop.Address : stop.Name;

  private static string Duration(double seconds) =>
    $"{(int)(seconds / 3600)}h {(int)(seconds % 3600 / 60):00}m";

  private static string TimeDifference(double value) =>
    $"{(value < 0 ? "−" : "+")}{Duration(Math.Abs(value))}";

  public void Dispose()
  {
    _disposed = true;
    _generation++;
    _lifetime.Cancel();
    _lifetime.Dispose();
  }
}
