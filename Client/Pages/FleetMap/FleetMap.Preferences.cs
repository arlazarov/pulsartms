using System.Security.Claims;
using System.Text.Json;
using Client.Models.DTO.Planning;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace Client.Pages.FleetMap;

public partial class FleetMap
{
  [CascadingParameter]
  private Task<AuthenticationState>? AuthenticationStateTask { get; set; }

  private string? _preferencesKey;
  private bool _preferencesLoaded;

  private async Task RestoreMapPreferencesAsync()
  {
    if (_preferencesLoaded)
      return;
    try
    {
      if (AuthenticationStateTask is null)
        return;
      var state = await AuthenticationStateTask.WaitAsync(_lifetime.Token);
      if (
        _disposed
        || state.User.Identity?.IsAuthenticated != true
        || !Guid.TryParse(
          state.User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
          out var userId
        )
      )
        return;
      _preferencesKey = $"pulsartms.fleet-map.preferences.{userId:D}";
      var json = await JS.InvokeAsync<string?>(
        "localStorage.getItem",
        _lifetime.Token,
        _preferencesKey
      );
      if (_disposed || string.IsNullOrWhiteSpace(json))
        return;
      var saved = JsonSerializer.Deserialize<MapLayerPreferences>(
        json,
        MapJsonOptions
      );
      if (saved is null)
        return;
      ShowFuelStations = saved.ShowFuelStations;
      ShowTraffic = saved.ShowTraffic;
      ShowNextLoads = saved.ShowNextLoads;
    }
    catch (Exception ex) when (ex is JSException or JsonException) { }
    catch (OperationCanceledException) when (_disposed) { }
    finally
    {
      _preferencesLoaded = true;
    }
  }

  // Asked for beside the map rather than before it: a wrong price basis for a
  // moment costs a shade of green, a blocked start costs the whole map.
  private async Task ReadFuelPricingBasisAsync()
  {
    var result = await Api.GetAsync<PlanningSettingsState>(
      "api/settings/planning",
      _lifetime.Token
    );
    if (
      _disposed
      || !result.Success
      || result.Response is not { } state
      || state.Preferences.UseIfta == UseIfta
    )
      return;
    UseIfta = state.Preferences.UseIfta;
    try
    {
      if (_map is not null)
        await _map.InvokeVoidAsync("setIfta", UseIfta);
      if (!_disposed)
        await OnDateChanged();
    }
    catch (JSException) { }
    catch (OperationCanceledException) when (_disposed) { }
  }

  private async Task SaveMapPreferencesAsync()
  {
    if (_disposed || _preferencesKey is null)
      return;
    var saved = new MapLayerPreferences
    {
      ShowFuelStations = ShowFuelStations,
      ShowTraffic = ShowTraffic,
      ShowNextLoads = ShowNextLoads,
    };
    try
    {
      await JS.InvokeVoidAsync(
        "localStorage.setItem",
        _lifetime.Token,
        _preferencesKey,
        JsonSerializer.Serialize(saved, MapJsonOptions)
      );
    }
    catch (JSException) { }
    catch (OperationCanceledException) when (_disposed) { }
  }

  // The fuel price basis left this record when the IFTA switch left the map;
  // a stored UseIfta from an older visit is simply ignored.
  private sealed record MapLayerPreferences
  {
    public bool ShowFuelStations { get; init; }
    public bool ShowTraffic { get; init; } = true;
    public bool ShowNextLoads { get; init; }
  }
}
