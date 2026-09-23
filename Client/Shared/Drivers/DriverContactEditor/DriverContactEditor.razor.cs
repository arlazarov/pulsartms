using Client.Models.DTO.Fleet;
using Client.Services;
using Microsoft.AspNetCore.Components;

namespace Client.Shared.Drivers.DriverContactEditor;

// A driver's phone, email and WhatsApp number. Phone and email follow the
// telematics source until they are set or cleared here; the server checks
// every value, and a draft is kept when a save is refused.
public partial class DriverContactEditor : IDisposable
{
  [Inject]
  private ApiService Api { get; set; } = default!;

  [Parameter, EditorRequired]
  public Guid DriverId { get; set; }

  [Parameter]
  public EventCallback<DriverContactState> Saved { get; set; }

  private readonly CancellationTokenSource _lifetime = new();
  private DriverContactState? _state;
  private Guid _loadedDriver;
  private string _phone = "",
    _email = "",
    _whatsApp = "";
  private bool _phoneFromSource,
    _emailFromSource,
    _loading,
    _saving,
    _saved,
    _disposed;
  private string? _error;

  private string Id => $"driver-contact-{DriverId:N}";

  private bool Changed =>
    _state is not null
    && (
      _phoneFromSource != !_state.Phone.IsLocal
      || _emailFromSource != !_state.Email.IsLocal
      || !_phoneFromSource && _phone != (_state.Phone.Value ?? "")
      || !_emailFromSource && _email != (_state.Email.Value ?? "")
      || _whatsApp != (_state.WhatsAppPhone ?? "")
    );

  private bool CanCopyPhone =>
    _state is { Phone.Usable: true }
    && _phoneFromSource == !_state.Phone.IsLocal
    && string.IsNullOrWhiteSpace(_whatsApp);

  protected override Task OnParametersSetAsync() =>
    _loadedDriver == DriverId ? Task.CompletedTask : LoadAsync();

  private async Task LoadAsync()
  {
    if (_disposed || _saving)
      return;
    _loadedDriver = DriverId;
    _loading = true;
    _error = null;
    _saved = false;
    var result = await Api.GetAsync<DriverContactState>(
      $"api/drivers/{DriverId}/contact",
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
    Show(result.Response);
  }

  private void Show(DriverContactState state)
  {
    _state = state;
    Reset();
  }

  private void Reset()
  {
    if (_state is null)
      return;
    _phoneFromSource = !_state.Phone.IsLocal;
    _emailFromSource = !_state.Email.IsLocal;
    _phone = _state.Phone.Value ?? "";
    _email = _state.Email.Value ?? "";
    _whatsApp = _state.WhatsAppPhone ?? "";
  }

  private void FollowPhone(ChangeEventArgs args)
  {
    _phoneFromSource = args.Value is true;
    if (_phoneFromSource && _state is not null)
      _phone = _state.Phone.Source ?? "";
    _saved = false;
  }

  private void FollowEmail(ChangeEventArgs args)
  {
    _emailFromSource = args.Value is true;
    if (_emailFromSource && _state is not null)
      _email = _state.Email.Source ?? "";
    _saved = false;
  }

  // Copying is an explicit choice by the dispatcher; the number is still
  // checked and saved as the WhatsApp number in its own right.
  private void CopyPhone() => _whatsApp = _state?.Phone.Value ?? "";

  private string Text(ChangeEventArgs args)
  {
    _saved = false;
    return args.Value?.ToString() ?? "";
  }

  private static string SourceText(DriverContactValue value) =>
    value.Source is null ? "Source: not provided."
    : value.IsLocal ? $"Source: {value.Source}. Not used while set here."
    : value.Usable ? $"Source: {value.Source}."
    : $"Source: {value.Source}. Not a complete number or address.";

  private async Task SaveAsync()
  {
    if (_state is null || _saving || _loading || _disposed || !Changed)
      return;
    _saving = true;
    _saved = false;
    _error = null;
    var result = await Api.PutAsync<DriverContactUpdate, DriverContactState>(
      $"api/drivers/{DriverId}/contact",
      new(
        _state.Revision,
        _phoneFromSource ? null : _phone,
        _phoneFromSource,
        _emailFromSource ? null : _email,
        _emailFromSource,
        _whatsApp
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
    Show(result.Response);
    _saved = true;
    await Saved.InvokeAsync(result.Response);
  }

  public void Dispose()
  {
    _disposed = true;
    _lifetime.Cancel();
    _lifetime.Dispose();
  }
}
