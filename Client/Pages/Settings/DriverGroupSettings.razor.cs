using Client.Models.DTO.DriverGroups;
using Client.Models.DTO.Mileage;
using Client.Services;
using Microsoft.AspNetCore.Components;

namespace Client.Pages.Settings;

// A dispatcher makes, names, fills, changes and removes their own driver
// groups here. A refused save keeps the draft; the choice of group is made
// on the pages that list drivers.
public partial class DriverGroupSettings : IDisposable
{
  private const string Path = "api/driver-groups";

  [Inject]
  private ApiService Api { get; set; } = default!;

  [Inject]
  private ChosenDriverGroup Groups { get; set; } = default!;

  private readonly CancellationTokenSource _lifetime = new();
  private List<MileageDriverOption>? _drivers;
  private readonly HashSet<Guid> _chosen = [];
  private DriverGroupView? _group;
  private string _name = "",
    _filter = "";
  private Guid? _removing;
  private bool _editing,
    _busy,
    _saved,
    _disposed;
  private string? _error;

  private IEnumerable<MileageDriverOption> Shown =>
    (_drivers ?? [])
      .Where(x =>
        _filter.Trim().Length == 0
        || x.Name.Contains(_filter.Trim(), StringComparison.OrdinalIgnoreCase)
        || _chosen.Contains(x.Id)
      )
      .OrderByDescending(x => _chosen.Contains(x.Id))
      .ThenByDescending(x => x.IsActive)
      .ThenBy(x => x.Name);

  protected override async Task OnInitializedAsync()
  {
    _error = await Groups.RefreshAsync(_lifetime.Token);
    var drivers = await Api.GetAsync<MileageFleetList<MileageDriverOption>>(
      "api/fleet/drivers",
      _lifetime.Token
    );
    if (_disposed)
      return;
    if (drivers.Success && drivers.Response is { } list)
      _drivers = list.Items;
    else
      _error ??= drivers.ErrorMessage ?? "Drivers could not be loaded.";
  }

  private static string Count(DriverGroupView group) =>
    group.Drivers.Count == 1 ? "1 driver" : $"{group.Drivers.Count} drivers";

  private void Open(DriverGroupView? group)
  {
    _group = group;
    _name = group?.Name ?? "";
    _filter = "";
    _chosen.Clear();
    _chosen.UnionWith(group?.Drivers ?? []);
    _editing = true;
    _saved = false;
    _error = null;
  }

  private void Close()
  {
    _editing = false;
    _group = null;
  }

  private void Toggle(Guid driver, bool chosen)
  {
    if (chosen)
      _chosen.Add(driver);
    else
      _chosen.Remove(driver);
  }

  private async Task SaveAsync()
  {
    _busy = true;
    _error = null;
    var request = new DriverGroupRequest(
      _name.Trim(),
      [.. _chosen],
      _group?.Revision ?? 0
    );
    var result = _group is { } group
      ? await Api.PutAsync<DriverGroupRequest, DriverGroupView>(
        $"{Path}/{group.Id}",
        request,
        _lifetime.Token
      )
      : await Api.PostAsync<DriverGroupRequest, DriverGroupView>(
        Path,
        request,
        _lifetime.Token
      );
    if (_disposed)
      return;
    _busy = false;
    if (!result.Success)
    {
      _error = result.ErrorMessage ?? "The group could not be saved.";
      return;
    }
    _editing = false;
    _saved = true;
    _error = await Groups.ReloadAsync();
  }

  private async Task RemoveAsync(Guid id)
  {
    _busy = true;
    _error = null;
    var result = await Api.DeleteAsync<bool>($"{Path}/{id}", _lifetime.Token);
    if (_disposed)
      return;
    _busy = false;
    _removing = null;
    if (!result.Success)
      _error = result.ErrorMessage ?? "The group could not be removed.";
    _error ??= await Groups.ReloadAsync();
  }

  public void Dispose()
  {
    _disposed = true;
    _lifetime.Cancel();
    _lifetime.Dispose();
  }
}
