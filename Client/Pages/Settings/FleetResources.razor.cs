using System.ComponentModel.DataAnnotations;
using Client.Models.DTO.Fleet;
using Client.Services;
using Microsoft.AspNetCore.Components;

namespace Client.Pages.Settings;

public partial class FleetResources : IDisposable
{
  [Inject]
  private ApiService Api { get; set; } = default!;

  [Parameter]
  public string Kind { get; set; } = "trucks";

  private readonly CancellationTokenSource _lifetime = new();
  private CancellationTokenSource? _read;
  private FleetConfigurationList? _list;
  private FleetConfigurationState? _editing;
  private ResourceForm _form = new();
  private string? _loadedKind;
  private string _search = "";
  private string? _error;
  private string? _saved;
  private int _page = 1;
  private bool _loading;
  private bool _saving;
  private bool _restore;
  private bool _disposed;
  private string Title =>
    Kind switch
    {
      "trucks" => "Trucks",
      "trailers" => "Trailers",
      "drivers" => "Drivers",
      _ => "Fleet resources",
    };
  private bool IsDriver => Kind == "drivers";
  private bool ValidKind => Kind is "trucks" or "trailers" or "drivers";

  protected override async Task OnParametersSetAsync()
  {
    Kind ??= "trucks";
    if (_loadedKind == Kind)
      return;
    _loadedKind = Kind;
    _page = 1;
    _search = "";
    _list = null;
    CancelEdit();
    await LoadAsync();
  }

  private CancellationTokenSource StartRead()
  {
    _read?.Cancel();
    _read?.Dispose();
    _read = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
    return _read;
  }

  private async Task LoadAsync()
  {
    if (!ValidKind || _disposed || _saving)
      return;
    var owner = StartRead();
    _loading = true;
    _error = null;
    var url =
      $"api/settings/fleet/{Kind}?page={_page}"
      + $"&search={Uri.EscapeDataString(_search)}";
    var result = await Api.GetAsync<FleetConfigurationList>(url, owner.Token);
    if (_disposed || owner != _read || owner.IsCancellationRequested)
      return;
    _loading = false;
    if (result.Success && result.Response is not null)
      _list = result.Response;
    else
      _error = result.ErrorMessage;
  }

  private async Task SearchAsync()
  {
    if (_loading || _saving)
      return;
    _page = 1;
    CancelEdit();
    await LoadAsync();
  }

  private async Task PageAsync(int page)
  {
    if (_loading || _saving)
      return;
    _page = page;
    CancelEdit();
    await LoadAsync();
  }

  private async Task EditAsync(Guid id)
  {
    if (_saving || _disposed)
      return;
    CancelEdit();
    var owner = StartRead();
    _loading = true;
    var result = await Api.GetAsync<FleetConfigurationState>(
      $"api/settings/fleet/{Kind}/{id}",
      owner.Token
    );
    if (_disposed || owner != _read || owner.IsCancellationRequested)
      return;
    _loading = false;
    if (!result.Success || result.Response is null)
    {
      _error = result.ErrorMessage;
      return;
    }
    _editing = result.Response;
    var row = _editing.Resource;
    _form = new()
    {
      Name = row.Name,
      Vin = row.Vin,
      FuelCard = _editing.FuelCard,
      IsActive = row.IsActive,
    };
  }

  private void CancelEdit()
  {
    if (_saving)
      return;
    _editing = null;
    _form.FuelCard = "";
    _restore = false;
    _error = null;
  }

  private Task SaveAsync() => SaveAsync(false);

  private async Task SaveAsync(bool imported)
  {
    if (_saving || _loading || _editing is null || _disposed)
      return;
    _saving = true;
    _error = null;
    _saved = null;
    var kind = Kind;
    var resource = _editing.Resource;
    var update = new FleetConfigurationUpdate(
      resource.Revision,
      _form.Name,
      _form.Vin,
      _form.FuelCard,
      _form.IsActive,
      imported
    );
    var result = await Api.PutAsync<
      FleetConfigurationUpdate,
      FleetConfigurationState
    >($"api/settings/fleet/{kind}/{resource.Id}", update, _lifetime.Token);
    if (_disposed)
      return;
    _saving = false;
    if (kind != Kind)
    {
      CancelEdit();
      await LoadAsync();
      return;
    }
    if (!result.Success || result.Response is null)
    {
      _error = result.ErrorMessage;
      return;
    }
    CancelEdit();
    _saved = imported ? "Imported configuration restored." : "Changes saved.";
    await LoadAsync();
  }

  public void Dispose()
  {
    _disposed = true;
    _lifetime.Cancel();
    _read?.Cancel();
    _read?.Dispose();
    _lifetime.Dispose();
    _editing = null;
    _form.FuelCard = "";
  }

  private sealed class ResourceForm
  {
    [Required, StringLength(200)]
    public string Name { get; set; } = "";

    [StringLength(17)]
    public string Vin { get; set; } = "";

    [StringLength(50)]
    public string FuelCard { get; set; } = "";
    public bool IsActive { get; set; }
  }
}
