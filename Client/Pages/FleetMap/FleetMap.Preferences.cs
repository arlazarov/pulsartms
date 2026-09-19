using System.Security.Claims;
using System.Text.Json;
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
      UseIfta = saved.UseIfta;
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

  private async Task SaveMapPreferencesAsync()
  {
    if (_disposed || _preferencesKey is null)
      return;
    var saved = new MapLayerPreferences
    {
      UseIfta = UseIfta,
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

  private sealed record MapLayerPreferences
  {
    public bool UseIfta { get; init; }
    public bool ShowFuelStations { get; init; }
    public bool ShowTraffic { get; init; } = true;
    public bool ShowNextLoads { get; init; }
  }
}
