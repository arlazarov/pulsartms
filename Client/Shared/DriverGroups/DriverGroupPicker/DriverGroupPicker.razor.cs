using Client.Services;
using Microsoft.AspNetCore.Components;

namespace Client.Shared.DriverGroups.DriverGroupPicker;

// The one switch between the dispatcher's own driver groups and all
// drivers, the same on every page that lists drivers' work. The choice is
// the dispatcher's and holds on every page and device.
public partial class DriverGroupPicker : IDisposable
{
  [Inject]
  private ChosenDriverGroup Groups { get; set; } = default!;

  [Parameter, EditorRequired]
  public string Id { get; set; } = "";

  private readonly CancellationTokenSource _lifetime = new();
  private bool _busy,
    _disposed;
  private string? _error;

  private string Title =>
    Groups.Selected is { } group
      ? $"Only {group.Name}'s drivers are shown, on every page."
      : "All drivers you can see.";

  protected override async Task OnInitializedAsync()
  {
    Groups.Changed += OnChanged;
    _error = await Groups.RefreshAsync(_lifetime.Token);
  }

  private async Task ChooseAsync(ChangeEventArgs args)
  {
    _busy = true;
    _error = null;
    var value = args.Value?.ToString();
    _error = await Groups.SelectAsync(
      Guid.TryParse(value, out var id) ? id : null
    );
    if (_disposed)
      return;
    _busy = false;
  }

  private void OnChanged() =>
    _ = InvokeAsync(() =>
    {
      if (!_disposed)
        StateHasChanged();
    });

  public void Dispose()
  {
    _disposed = true;
    Groups.Changed -= OnChanged;
    _lifetime.Cancel();
    _lifetime.Dispose();
  }
}
