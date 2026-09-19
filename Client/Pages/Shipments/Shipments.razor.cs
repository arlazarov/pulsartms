using System.Net;
using System.Text.Json;
using Client.Models.DTO.Shipments;
using Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

namespace Client.Pages.Shipments;

public partial class Shipments : IDisposable
{
  [Parameter]
  public Guid LoadId { get; set; }

  [Inject]
  private ApiService Api { get; set; } = default!;
  private static readonly string[] Packages =
  [
    "box",
    "carton",
    "crate",
    "bag",
    "drum",
    "piece",
    "pallet",
    "bulk",
  ];
  private readonly CancellationTokenSource _lifetime = new();
  private List<ShipmentStopOption> _stops = [];
  private List<Shipment> _rows = [];
  private List<ShipmentIssue> _issues = [];
  private Shipment? _draft;
  private SaveShipment? _pending;
  private Guid _loadedId;
  private bool _loading,
    _saving,
    _dirty,
    _disposed,
    _navigationBlocked,
    _checked;
  private string? _error;
  private bool Locked => _saving || _pending is not null;

  protected override async Task OnParametersSetAsync()
  {
    if (_loadedId == LoadId)
      return;
    _loadedId = LoadId;
    _draft = null;
    _pending = null;
    _dirty = false;
    await LoadAsync();
  }

  private async Task LoadAsync()
  {
    var id = LoadId;
    _loading = true;
    var result = await Api.GetAsync<List<Shipment>>(
      $"api/shipments?loadId={id}",
      _lifetime.Token
    );
    if (_disposed || id != LoadId)
      return;
    var stops = await Api.GetAsync<List<ShipmentStopOption>>(
      $"api/shipments/stops?loadId={id}",
      _lifetime.Token
    );
    if (_disposed || id != LoadId)
      return;
    _stops = stops.Response ?? [];
    _loading = false;
    if (result.Success)
      _rows = result.Response ?? [];
    _error = result.Success ? null : result.ErrorMessage;
  }

  private void CopyParty(bool pickup)
  {
    if (Locked || _draft is null)
      return;
    var id = pickup ? _draft.PickupStopId : _draft.DeliveryStopId;
    var stop = _stops.SingleOrDefault(x => x.Id == id);
    if (stop is null)
      return;
    var party = new ShipmentParty
    {
      Name = stop.Name,
      AddressLine1 = stop.Address,
      City = stop.City,
      Region = stop.Region,
      Country = stop.Country,
      PostalCode = stop.PostalCode,
    };
    if (pickup)
      _draft.Shipper = party;
    else
      _draft.Consignee = party;
    Changed();
  }

  private void Create()
  {
    _draft = new() { Id = Guid.NewGuid(), LoadId = LoadId };
    _dirty = true;
    _checked = false;
  }

  private void Edit(Shipment row)
  {
    _draft = Clone(row);
    _dirty = false;
    _checked = false;
  }

  private void Changed()
  {
    if (Locked)
      return;
    _dirty = true;
    _checked = false;
  }

  private void AddCommodity()
  {
    if (Locked || _draft is null)
      return;
    _draft.Commodities.Add(new() { Id = Guid.NewGuid() });
    Changed();
  }

  private void Remove(ShipmentCommodity row)
  {
    if (Locked || _draft is null)
      return;
    _draft.Commodities.Remove(row);
    Changed();
  }

  private async Task Discard()
  {
    if (_saving)
      return;
    _draft = null;
    _pending = null;
    _dirty = _navigationBlocked = false;
    await LoadAsync();
  }

  private async Task SaveAsync()
  {
    if (_saving || !_dirty || _draft is null)
      return;
    var id = LoadId;
    _pending ??= new(Guid.NewGuid(), _draft.Revision, Clone(_draft));
    _saving = true;
    var result = await Api.PutAsync<SaveShipment, Shipment>(
      "api/shipments",
      _pending,
      _lifetime.Token
    );
    if (_disposed || id != LoadId)
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
    _rows.RemoveAll(x => x.Id == _draft.Id);
    _rows.Add(Clone(_draft));
    _pending = null;
    _dirty = _navigationBlocked = false;
    _error = null;
  }

  private async Task CheckAsync()
  {
    if (Locked || _draft is null)
      return;
    var draft = _draft;
    var signature = JsonSerializer.Serialize(draft);
    var result = await Api.PostAsync<Shipment, List<ShipmentIssue>>(
      "api/shipments/check",
      Clone(draft),
      _lifetime.Token
    );
    if (
      _disposed
      || _draft != draft
      || signature != JsonSerializer.Serialize(draft)
    )
      return;
    _issues = result.Response ?? [];
    _checked = result.Success;
    _error = result.Success ? null : result.ErrorMessage;
  }

  private void BeforeNavigation(LocationChangingContext context)
  {
    if (!_dirty && !_saving)
      return;
    context.PreventNavigation();
    _navigationBlocked = true;
  }

  private static string Display(Shipment row) =>
    string.IsNullOrWhiteSpace(row.BillOfLading)
      ? $"Shipment · revision {row.Revision}"
      : row.BillOfLading;

  private static T Clone<T>(T value) =>
    JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;

  public void Dispose()
  {
    _disposed = true;
    _lifetime.Cancel();
    _lifetime.Dispose();
    GC.SuppressFinalize(this);
  }
}
