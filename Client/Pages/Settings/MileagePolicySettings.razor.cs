using Client.Models.DTO.Mileage;
using Client.Services;
using Microsoft.AspNetCore.Components;

namespace Client.Pages.Settings;

public partial class MileagePolicySettings : IDisposable
{
  [Inject]
  private ApiService Api { get; set; } = default!;

  private readonly CancellationTokenSource _lifetime = new();
  private MileagePolicyState? _confirmed;
  private PolicyForm _form = new();
  private bool _loading;
  private bool _editing;
  private bool _saving;
  private bool _saved;
  private bool _disposed;
  private string? _error;
  private static readonly (string Value, string Label)[] Targets =
  [
    ("previous", "Previous load"),
    ("next", "Next load"),
    ("unallocated", "Unallocated"),
  ];

  protected override Task OnInitializedAsync() => LoadAsync();

  private async Task LoadAsync()
  {
    if (_disposed || _loading || _saving)
      return;
    _loading = true;
    _error = null;
    var result = await Api.GetAsync<MileagePolicyState>(
      "api/settings/mileage-policy",
      _lifetime.Token
    );
    if (_disposed)
      return;
    _loading = false;
    if (!result.Success || result.Response is null)
    {
      _error = result.ErrorMessage;
      return;
    }
    _confirmed = result.Response;
    RestoreForm();
  }

  private void RestoreForm()
  {
    if (_confirmed is null)
      return;
    _form = new()
    {
      YardReturn = _confirmed.YardReturn,
      Home = _confirmed.Home,
      Maintenance = _confirmed.Maintenance,
      Reposition = _confirmed.Reposition,
    };
    _editing = false;
  }

  private void Edit()
  {
    _editing = true;
    _saved = false;
    _error = null;
  }

  private void Cancel()
  {
    if (_saving)
      return;
    RestoreForm();
    _error = null;
  }

  private async Task SaveAsync()
  {
    if (!_editing || _saving || _loading || _confirmed is null || _disposed)
      return;
    _saving = true;
    _saved = false;
    _error = null;
    var result = await Api.PutAsync<MileagePolicyUpdate, MileagePolicyState>(
      "api/settings/mileage-policy",
      new(
        _confirmed.Revision,
        _form.YardReturn,
        _form.Home,
        _form.Maintenance,
        _form.Reposition
      ),
      _lifetime.Token
    );
    if (_disposed)
      return;
    _saving = false;
    if (!result.Success || result.Response is null)
    {
      _error = result.ErrorMessage;
      return;
    }
    _confirmed = result.Response;
    RestoreForm();
    _saved = true;
  }

  public void Dispose()
  {
    _disposed = true;
    _lifetime.Cancel();
    _lifetime.Dispose();
  }

  private sealed class PolicyForm
  {
    public string YardReturn { get; set; } = "unallocated";
    public string Home { get; set; } = "unallocated";
    public string Maintenance { get; set; } = "unallocated";
    public string Reposition { get; set; } = "unallocated";
  }
}
