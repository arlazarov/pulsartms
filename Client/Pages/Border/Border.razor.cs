using System.Net;
using System.Text.Json;
using Client.Models.DTO.Border;
using Client.Models.DTO.Mileage;
using Client.Models.DTO.Shipments;
using Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

namespace Client.Pages.Border;

public partial class Border : IDisposable
{
  [Inject]
  private ApiService Api { get; set; } = default!;
  private static readonly string[] Tabs =
  [
    "Crossing",
    "Shipments",
    "Crew & equipment",
  ];
  private readonly CancellationTokenSource _lifetime = new();
  private List<BorderPort> _ports = [];
  private List<BorderSummary> _rows = [];
  private List<BorderShipmentOption> _matches = [];
  private List<BorderAssignmentOption> _assignments = [];
  private List<ShipmentIssue> _issues = [];
  private List<MileageDriverOption> _drivers = [];
  private List<MileageUnitOption> _trucks = [],
    _trailers = [];
  private BorderCrossing? _draft;
  private SaveBorderCrossing? _pending;
  private bool _loading,
    _saving,
    _dirty,
    _checked,
    _disposed,
    _navigationBlocked;
  private int _offset;
  private string _search = "",
    _tab = "Crossing";
  private string? _error;
  private bool Locked => _saving || _pending is not null;

  protected override async Task OnInitializedAsync()
  {
    await LoadAsync();
    var driverTask = Api.GetAsync<MileageFleetList<MileageDriverOption>>(
      "api/fleet/drivers",
      _lifetime.Token
    );
    var truckTask = Api.GetAsync<MileageFleetList<MileageUnitOption>>(
      "api/fleet/trucks",
      _lifetime.Token
    );
    var trailerTask = Api.GetAsync<MileageFleetList<MileageUnitOption>>(
      "api/fleet/trailers",
      _lifetime.Token
    );
    var portTask = Api.GetAsync<List<BorderPort>>(
      "api/border/ports",
      _lifetime.Token
    );
    await Task.WhenAll(driverTask, truckTask, trailerTask, portTask);
    var ports = await portTask;
    var drivers = await driverTask;
    var trucks = await truckTask;
    var trailers = await trailerTask;
    if (_disposed)
      return;
    _ports = ports.Response ?? [];
    _drivers = drivers.Response?.Items ?? [];
    _trucks = trucks.Response?.Items ?? [];
    _trailers = trailers.Response?.Items ?? [];
    if (!drivers.Success || !trucks.Success || !trailers.Success)
      _error = "Fleet options are unavailable. Reload the page to retry.";
  }

  private async Task LoadAsync()
  {
    _loading = true;
    var result = await Api.GetAsync<List<BorderSummary>>(
      $"api/border?offset={_offset}",
      _lifetime.Token
    );
    if (_disposed)
      return;
    _rows = result.Response ?? [];
    _error = result.Success ? null : result.ErrorMessage;
    _loading = false;
  }

  private async Task Previous()
  {
    _offset = Math.Max(0, _offset - 25);
    await LoadAsync();
  }

  private async Task Next()
  {
    _offset += 25;
    await LoadAsync();
  }

  private void Create()
  {
    _draft = new() { Id = Guid.NewGuid() };
    _dirty = true;
    _checked = false;
    _tab = "Crossing";
    _error = null;
  }

  private async Task Open(Guid id)
  {
    _loading = true;
    var result = await Api.GetAsync<BorderCrossing>(
      $"api/border/{id}",
      _lifetime.Token
    );
    if (_disposed)
      return;
    _loading = false;
    _draft = result.Response;
    _error = result.Success ? null : result.ErrorMessage;
    _dirty = _checked = false;
    _tab = "Crossing";
  }

  private void Changed()
  {
    if (Locked)
      return;
    _dirty = true;
    _checked = false;
  }

  private void ResourceChanged()
  {
    if (_draft is null || Locked)
      return;
    _draft.SourceLegId = _draft.SourceStopId = null;
    _draft.SourceRevision = null;
    Changed();
  }

  private async Task Search()
  {
    if (Locked || _draft is null)
      return;
    var draft = _draft;
    var term = _search;
    var result = await Api.GetAsync<List<BorderShipmentOption>>(
      $"api/border/shipments?search={Uri.EscapeDataString(term)}",
      _lifetime.Token
    );
    if (_disposed || draft != _draft || term != _search)
      return;
    _matches = result.Response ?? [];
    _error = result.Success ? null : result.ErrorMessage;
  }

  private async Task AddShipment(BorderShipmentOption option)
  {
    if (
      Locked
      || _draft is null
      || _draft.Shipments.Any(x => x.ShipmentId == option.Id)
    )
      return;
    var draft = _draft;
    var result = await Api.GetAsync<List<Shipment>>(
      $"api/shipments?loadId={option.LoadId}",
      _lifetime.Token
    );
    if (_disposed || _draft != draft || Locked)
      return;
    var shipment = result.Response?.SingleOrDefault(x => x.Id == option.Id);
    if (shipment is null)
    {
      _error = "Shipment is unavailable. Search again.";
      return;
    }
    if (draft.Shipments.Any(x => x.ShipmentId == option.Id))
      return;
    draft.Shipments.Add(
      new()
      {
        Id = Guid.NewGuid(),
        ShipmentId = shipment.Id,
        ShipmentRevision = shipment.Revision,
        Snapshot = shipment,
      }
    );
    Changed();
  }

  private async Task RefreshShipment(BorderShipment row)
  {
    if (Locked || _draft is null || row.Snapshot is null)
      return;
    var draft = _draft;
    var result = await Api.GetAsync<List<Shipment>>(
      $"api/shipments?loadId={row.Snapshot.LoadId}",
      _lifetime.Token
    );
    if (
      _disposed
      || _draft != draft
      || Locked
      || !draft.Shipments.Contains(row)
    )
      return;
    var shipment = result.Response?.SingleOrDefault(x =>
      x.Id == row.ShipmentId
    );
    if (shipment is null)
    {
      _error = "Source shipment is unavailable.";
      return;
    }
    row.Snapshot = shipment;
    row.ShipmentRevision = shipment.Revision;
    Changed();
  }

  private void RemoveShipment(BorderShipment row)
  {
    if (Locked || _draft is null)
      return;
    _draft.Shipments.Remove(row);
    Changed();
  }

  private async Task Assignments(Guid loadId)
  {
    if (Locked || _draft is null)
      return;
    var draft = _draft;
    var result = await Api.GetAsync<List<BorderAssignmentOption>>(
      $"api/border/assignments?loadId={loadId}",
      _lifetime.Token
    );
    if (_disposed || _draft != draft)
      return;
    _assignments = result.Response ?? [];
    _error = result.Success ? null : result.ErrorMessage;
    _tab = "Crew & equipment";
  }

  private void UseAssignment(BorderAssignmentOption option)
  {
    if (Locked || _draft is null)
      return;
    var oldCrew = _draft.Crew;
    var oldEquipment = _draft.Equipment;
    _draft.Crew = oldCrew.Where(x => x.Role == "passenger").ToList();
    foreach (
      var (driver, role) in new[]
      {
        (option.DriverId, "driver"),
        (option.CoDriverId, "co-driver"),
      }
    )
      if (driver.HasValue)
      {
        var member =
          oldCrew.FirstOrDefault(x => x.DriverId == driver)
          ?? new BorderCrew
          {
            Id = Guid.NewGuid(),
            DriverId = driver,
            DisplayName =
              _drivers.FirstOrDefault(x => x.Id == driver)?.Name ?? "",
          };
        member.Role = role;
        _draft.Crew.Add(member);
      }
    _draft.Equipment = [];
    var truck =
      oldEquipment.FirstOrDefault(x => x.TruckId == option.TruckId)
      ?? new BorderEquipment
      {
        Id = Guid.NewGuid(),
        Kind = "truck",
        TruckId = option.TruckId,
        UnitNumber =
          _trucks.FirstOrDefault(x => x.Id == option.TruckId)?.UnitNumber ?? "",
      };
    _draft.Equipment.Add(truck);
    if (option.TrailerId is { } trailerId)
      _draft.Equipment.Add(
        oldEquipment.FirstOrDefault(x => x.TrailerId == trailerId)
          ?? new BorderEquipment
          {
            Id = Guid.NewGuid(),
            Kind = "trailer",
            TrailerId = trailerId,
            UnitNumber =
              _trailers.FirstOrDefault(x => x.Id == trailerId)?.UnitNumber
              ?? "",
          }
      );
    foreach (var shipment in _draft.Shipments)
      if (!_draft.Equipment.Any(x => x.Id == shipment.EquipmentId))
        shipment.EquipmentId = null;
    _draft.SourceLegId = option.LegId;
    _draft.SourceStopId = option.StopId;
    _draft.SourceRevision = option.Revision;
    Changed();
  }

  private async Task Discard()
  {
    if (_saving)
      return;
    _draft = null;
    _pending = null;
    _dirty = _navigationBlocked = _checked = false;
    _matches.Clear();
    _assignments.Clear();
    await LoadAsync();
  }

  private async Task SaveAsync()
  {
    if (_draft is null || _saving || !_dirty)
      return;
    _pending ??= new(Guid.NewGuid(), _draft.Revision, Clone(_draft));
    _saving = true;
    var result = await Api.PutAsync<SaveBorderCrossing, BorderCrossing>(
      "api/border",
      _pending,
      _lifetime.Token
    );
    if (_disposed)
      return;
    _saving = false;
    if (!result.Success || result.Response is null)
    {
      _error = result.ErrorMessage;
      if (
        result.HttpStatusCode
        is HttpStatusCode.BadRequest
          or HttpStatusCode.Conflict
          or HttpStatusCode.Forbidden
          or HttpStatusCode.NotFound
      )
        _pending = null;
      return;
    }
    _draft = result.Response;
    _pending = null;
    _dirty = _navigationBlocked = false;
    _error = null;
  }

  private async Task CheckAsync()
  {
    if (_draft is null || Locked)
      return;
    var draft = _draft;
    var signature = JsonSerializer.Serialize(draft);
    var result = await Api.PostAsync<BorderCrossing, List<ShipmentIssue>>(
      "api/border/check",
      Clone(draft),
      _lifetime.Token
    );
    if (
      _disposed
      || _draft != draft
      || signature != JsonSerializer.Serialize(draft)
    )
      return;
    _checked = result.Success;
    _issues = result.Response ?? [];
    _error = result.Success ? null : result.ErrorMessage;
  }

  private void BeforeNavigation(LocationChangingContext context)
  {
    if (!_dirty && !_saving)
      return;
    context.PreventNavigation();
    _navigationBlocked = true;
  }

  private static T Clone<T>(T value) =>
    JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;

  private static string Label(BorderSummary row) =>
    string.IsNullOrWhiteSpace(row.Reference)
      ? $"Crossing · {row.DestinationCountry} · {row.PortOfEntry}"
      : row.Reference;

  public void Dispose()
  {
    _disposed = true;
    _lifetime.Cancel();
    _lifetime.Dispose();
    GC.SuppressFinalize(this);
  }
}
