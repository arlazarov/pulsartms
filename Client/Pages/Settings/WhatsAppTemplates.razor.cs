using Client.Models.DTO.Messaging;
using Client.Services;
using Microsoft.AspNetCore.Components;

namespace Client.Pages.Settings;

// The templates an administrator recorded as approved by Meta for the
// number the company sends from now. A refused entry keeps its draft; an
// answer for an earlier read never replaces a newer one.
public partial class WhatsAppTemplates : IDisposable
{
  private const string Path = "api/settings/integrations/whatsapp/templates";

  [Inject]
  private ApiService Api { get; set; } = default!;

  private readonly CancellationTokenSource _lifetime = new();
  private ApprovedTemplatesView? _view;
  private string _name = "",
    _language = "en_US",
    _text = "";
  private int _parameters;
  private Guid? _removing;
  private bool _loading,
    _adding,
    _busy,
    _added,
    _disposed;
  private string? _error;
  private int _read;

  private bool CanAdd =>
    !_busy
    && _name.Trim().Length > 0
    && _language.Trim().Length > 0
    && _text.Trim().Length > 0;

  protected override Task OnInitializedAsync() => LoadAsync();

  private async Task LoadAsync()
  {
    var generation = ++_read;
    _loading = true;
    _error = null;
    var result = await Api.GetAsync<ApprovedTemplatesView>(
      Path,
      _lifetime.Token
    );
    if (_disposed || generation != _read)
      return;
    _loading = false;
    if (result.Success && result.Response is { } view)
      _view = view;
    else
      _error = result.ErrorMessage ?? "Templates could not be loaded.";
  }

  // After Meta approves a PulsR template: the form, filled exactly as PulsR
  // defines it, for the administrator to record.
  private void Prefill(PulsrTemplateView template)
  {
    _name = template.Name;
    _language = template.Language;
    _parameters = template.Parameters;
    _text = template.Body;
    Open();
  }

  // The form opens on request, so the card shows no field until then.
  private void Open()
  {
    _adding = true;
    _added = false;
  }

  private async Task AddAsync()
  {
    if (!CanAdd)
      return;
    _busy = true;
    _added = false;
    _error = null;
    var result = await Api.PostAsync<
      ApprovedTemplateRequest,
      ApprovedTemplateView
    >(
      Path,
      new(_name.Trim(), _language.Trim(), _parameters, _text.Trim()),
      _lifetime.Token
    );
    if (_disposed)
      return;
    _busy = false;
    if (!result.Success)
    {
      _error = result.ErrorMessage ?? "The template could not be recorded.";
      return;
    }
    _name = "";
    _text = "";
    _parameters = 0;
    _adding = false;
    _added = true;
    await LoadAsync();
  }

  private async Task RemoveAsync(Guid id)
  {
    _busy = true;
    _added = false;
    _error = null;
    var result = await Api.DeleteAsync<bool>($"{Path}/{id}", _lifetime.Token);
    if (_disposed)
      return;
    _busy = false;
    _removing = null;
    if (!result.Success)
      _error = result.ErrorMessage ?? "The template could not be removed.";
    await LoadAsync();
  }

  public void Dispose()
  {
    _disposed = true;
    _lifetime.Cancel();
    _lifetime.Dispose();
  }
}
