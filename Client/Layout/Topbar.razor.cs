using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

namespace Client.Layout;

public partial class Topbar : IDisposable
{
  [Inject]
  private NavigationManager Navigation { get; set; } = default!;

  [Inject]
  private TimeProvider Clock { get; set; } = default!;

  private DateTime _now;
  private ITimer? _timer;

  private (string Section, string Page) Place =>
    Navigation.ToAbsoluteUri(Navigation.Uri).AbsolutePath switch
    {
      var path when path.StartsWith("/fleet/map", StringComparison.Ordinal) => (
        "Operations",
        "Fleet Map"
      ),
      var path when path.StartsWith("/dispatch", StringComparison.Ordinal) => (
        "Operations",
        "Dispatch"
      ),
      var path when path.StartsWith("/messages", StringComparison.Ordinal) => (
        "Operations",
        "Messages"
      ),
      var path when path.StartsWith("/customers", StringComparison.Ordinal) => (
        "Directory",
        "Customers & brokers"
      ),
      var path when path.StartsWith("/border", StringComparison.Ordinal) => (
        "Operations",
        "Border"
      ),
      var path
        when path.StartsWith("/settings/fleet", StringComparison.Ordinal) => (
        "Administration",
        "Fleet"
      ),
      var path when path.StartsWith("/users", StringComparison.Ordinal) => (
        "Administration",
        "Users"
      ),
      var path
        when path.StartsWith("/settings/personal", StringComparison.Ordinal) =>
        ("Account", "Personal settings"),
      var path when path.StartsWith("/settings", StringComparison.Ordinal) => (
        "Administration",
        "Settings"
      ),
      _ => ("PulsR", "Home"),
    };

  protected override void OnInitialized()
  {
    Navigation.LocationChanged += OnLocationChanged;
    _now = Clock.GetLocalNow().DateTime;
    // The minute turns over at most once a minute; a page left open all
    // day costs one render a minute here.
    _timer = Clock.CreateTimer(
      _ => InvokeAsync(Tick),
      null,
      TimeSpan.FromSeconds(60 - _now.Second),
      TimeSpan.FromMinutes(1)
    );
  }

  private void Tick()
  {
    _now = Clock.GetLocalNow().DateTime;
    StateHasChanged();
  }

  private void OnLocationChanged(object? sender, LocationChangedEventArgs e) =>
    InvokeAsync(StateHasChanged);

  public void Dispose()
  {
    Navigation.LocationChanged -= OnLocationChanged;
    _timer?.Dispose();
  }
}
