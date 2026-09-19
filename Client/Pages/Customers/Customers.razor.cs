using System.Text.Json;
using Client.Models.DTO.Dispatch.Workspace;
using Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

namespace Client.Pages.Customers;

public partial class Customers : IDisposable
{
  [Inject]
  private ApiService Api { get; set; } = default!;

  private readonly CancellationTokenSource _lifetime = new();
  private List<BrokerProfile> _matches = [];
  private BrokerProfile? _editing;
  private string _search = "";
  private string? _error;
  private string? _notice;
  private bool _searched;
  private bool _loading;
  private bool _saving;
  private bool _dirty;
  private bool _disposed;
  private bool _navigationBlocked;

  private void Changed() => _dirty = true;

  private async Task SearchAsync()
  {
    if (_loading || _saving || _editing is not null || _disposed)
      return;
    if (_search.Trim().Length < 2)
    {
      _error = "Enter at least two characters of the company name.";
      return;
    }
    _loading = true;
    _error = _notice = null;
    var response = await Api.GetAsync<List<BrokerProfile>>(
      $"api/brokers?search={Uri.EscapeDataString(_search.Trim())}",
      _lifetime.Token
    );
    if (_disposed)
      return;
    _loading = false;
    _searched = response.Success;
    _matches = response.Success ? response.Response ?? [] : [];
    _error = response.Success
      ? null
      : response.ErrorMessage ?? "Could not load companies. Try again.";
  }

  private void Create()
  {
    if (_loading || _saving)
      return;
    _editing = new() { Id = Guid.NewGuid() };
    _dirty = false;
    _error = _notice = null;
  }

  private void Edit(BrokerProfile company)
  {
    if (_loading || _saving)
      return;
    _editing = JsonSerializer.Deserialize<BrokerProfile>(
      JsonSerializer.Serialize(company)
    );
    _dirty = false;
    _error = _notice = null;
  }

  private void Discard()
  {
    if (_saving)
      return;
    _editing = null;
    _dirty = _navigationBlocked = false;
    _error = null;
  }

  private async Task SaveAsync()
  {
    if (_saving || !_dirty || _editing is null || _disposed)
      return;
    if (string.IsNullOrWhiteSpace(_editing.Name))
    {
      _error = "Enter a company name.";
      return;
    }
    _saving = true;
    _error = _notice = null;
    var result = await Api.PutAsync<BrokerProfile, BrokerProfile>(
      "api/brokers",
      _editing,
      _lifetime.Token
    );
    if (_disposed)
      return;
    _saving = false;
    if (!result.Success || result.Response is null)
    {
      _error =
        result.ErrorMessage ?? "Save not confirmed. Your changes are retained.";
      return;
    }
    _editing = result.Response;
    _matches = _matches
      .Select(company => company.Id == _editing.Id ? _editing : company)
      .ToList();
    _dirty = _navigationBlocked = false;
    _notice = "Company saved.";
  }

  private void BeforeNavigation(LocationChangingContext context)
  {
    if (!_dirty && !_saving)
      return;
    context.PreventNavigation();
    _navigationBlocked = true;
  }

  public void Dispose()
  {
    _disposed = true;
    _lifetime.Cancel();
    _lifetime.Dispose();
    GC.SuppressFinalize(this);
  }
}
