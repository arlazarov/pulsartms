using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Client.Shared.CopyValue;

// A shown value whose words copy it (the truck's Location, a stop's
// Appointment). "Copied" is said only once the browser took the text; a
// refusal says so. The word belongs to the value and scope it was copied
// for: a copy that finishes after either changed says nothing.
public partial class CopyValue : IDisposable
{
  [Inject]
  private IJSRuntime JS { get; set; } = default!;

  [Parameter, EditorRequired]
  public string Value { get; set; } = "";

  // What the feedback belongs to, such as the chosen truck or stop.
  [Parameter]
  public object? Scope { get; set; }

  [Parameter]
  public string? Title { get; set; }

  [Parameter]
  public string? Class { get; set; }

  [Parameter]
  public string? StatusClass { get; set; }

  [Parameter]
  public RenderFragment? ChildContent { get; set; }

  private readonly CancellationTokenSource _lifetime = new();
  private bool _disposed;
  private string? _status;
  private int _version;
  private object? _scope;
  private string? _value;

  protected override void OnParametersSet()
  {
    if (!Equals(_scope, Scope) || _value != Value)
    {
      _version++;
      _status = null;
    }
    _scope = Scope;
    _value = Value;
  }

  private async Task CopyAsync()
  {
    var version = ++_version;
    var token = _lifetime.Token;
    string status;
    try
    {
      // No-break spaces only keep a shown value's words together on
      // screen; the clipboard gets ordinary spaces.
      await JS.InvokeVoidAsync(
        "navigator.clipboard.writeText",
        Value.Replace('\u00a0', ' ')
      );
      status = "Copied";
    }
    catch (JSException)
    {
      status = "Could not copy";
    }
    if (_disposed || version != _version)
      return;
    _status = status;
    StateHasChanged();
    try
    {
      await Task.Delay(TimeSpan.FromSeconds(2), token);
    }
    catch (OperationCanceledException)
    {
      return;
    }
    if (!_disposed && version == _version)
    {
      _status = null;
      StateHasChanged();
    }
  }

  public void Dispose()
  {
    _disposed = true;
    _lifetime.Cancel();
    _lifetime.Dispose();
  }
}
