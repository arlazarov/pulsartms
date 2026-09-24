using Client.Models.DTO.DriverGroups;
using Microsoft.AspNetCore.Components.Authorization;

namespace Client.Services;

// The dispatcher's chosen driver group: one choice for every page. The
// server applies it to what each page lists; this keeps the picker's view
// of it and tells the pages when it changes, so they read again. A group
// narrows only what the dispatcher may already see.
public sealed class ChosenDriverGroup : IDisposable
{
  private readonly ApiService _api;
  private readonly AuthenticationStateProvider _auth;
  private int _read;

  public ChosenDriverGroup(ApiService api, AuthenticationStateProvider auth)
  {
    _api = api;
    _auth = auth;
    _auth.AuthenticationStateChanged += OnAuthenticationChanged;
  }

  public DriverGroupsView? View { get; private set; }

  public DriverGroupView? Selected =>
    View?.Selected is { } id
      ? View.Groups.FirstOrDefault(x => x.Id == id)
      : null;

  // Raised after the choice or the groups change; pages read again.
  public event Action? Changed;

  // Read from the server each time a page shows the picker, so a choice
  // made in another tab is the one shown here on the next page.
  public async Task<string?> RefreshAsync(CancellationToken ct = default)
  {
    var read = ++_read;
    var result = await _api.GetAsync<DriverGroupsView>("api/driver-groups", ct);
    if (read != _read)
      return null;
    if (!result.Success || result.Response is null)
      return result.ErrorMessage ?? "Driver groups could not be loaded.";
    View = result.Response;
    return null;
  }

  public async Task<string?> SelectAsync(Guid? group)
  {
    var result = await _api.PutAsync<DriverGroupSelection, bool>(
      "api/driver-groups/selection",
      new(group)
    );
    if (!result.Success)
      return result.ErrorMessage ?? "The group could not be chosen.";
    _read++;
    View = View is null ? new(group, []) : View with { Selected = group };
    Changed?.Invoke();
    return null;
  }

  // After groups were created, changed or removed.
  public async Task<string?> ReloadAsync()
  {
    var error = await RefreshAsync();
    if (error is null)
      Changed?.Invoke();
    return error;
  }

  private void OnAuthenticationChanged(Task<AuthenticationState> state)
  {
    _read++;
    View = null;
    Changed?.Invoke();
  }

  public void Dispose() =>
    _auth.AuthenticationStateChanged -= OnAuthenticationChanged;
}
