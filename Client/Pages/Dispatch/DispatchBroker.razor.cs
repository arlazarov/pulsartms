using System.Text.Json;
using Client.Models.DTO.Dispatch.Workspace;
using Client.Services;
using Microsoft.AspNetCore.Components;

namespace Client.Pages.Dispatch;

public partial class DispatchBroker : IDisposable
{
  [Inject]
  private ApiService Api { get; set; } = default!;

  [Parameter, EditorRequired]
  public DispatchWorkspaceMetadata Metadata { get; set; } = new();

  [Parameter]
  public bool CanEdit { get; set; }

  [Parameter]
  public EventCallback Changed { get; set; }

  [Parameter]
  public EventCallback<bool> BusyChanged { get; set; }
  private readonly CancellationTokenSource _lifetime = new();
  private List<BrokerProfile> _matches = [];
  private BrokerProfile? _selected;
  private Guid _newId = Guid.NewGuid();
  private string _search = "";
  private string? _error;
  private string? _notice;
  private bool Busy { get; set; }
  private bool _disposed;
  private DispatchWorkspaceMetadata? _owner;

  protected override void OnParametersSet()
  {
    if (ReferenceEquals(_owner, Metadata))
      return;
    _owner = Metadata;
    _selected = null;
    _matches = [];
    _error = _notice = null;
    _newId = Guid.NewGuid();
  }

  private Task NotifyChanged() => Changed.InvokeAsync();

  private Task CompanyChanged()
  {
    Metadata.BrokerId = null;
    _selected = null;
    _newId = Guid.NewGuid();
    return NotifyChanged();
  }

  private async Task SearchAsync()
  {
    if (!CanEdit || Busy)
      return;
    var owner = Metadata;
    Busy = true;
    _error = _notice = null;
    var result = await Api.GetAsync<List<BrokerProfile>>(
      $"api/brokers?search={Uri.EscapeDataString(_search)}",
      _lifetime.Token
    );
    if (_disposed)
      return;
    Busy = false;
    if (!ReferenceEquals(owner, Metadata))
      return;
    _matches = result.Success ? result.Response ?? [] : [];
    _error = result.Success ? null : result.ErrorMessage;
    if (result.Success && _matches.Count == 0)
      _notice = "No match. Enter the broker below to create its defaults.";
  }

  private async Task SelectAsync(BrokerProfile profile)
  {
    if (!CanEdit || Busy)
      return;
    _selected = profile;
    Metadata.BrokerId = profile.Id;
    Metadata.BrokerCompany = profile.Name;
    Metadata.BrokerContact = profile.Contact;
    Metadata.BrokerPhone = profile.Phone;
    Metadata.BrokerEmail = profile.Email;
    Metadata.BillTo = profile.BillTo;
    Metadata.PaymentTerms = Copy(profile.Terms);
    _matches = [];
    _notice = "Broker selected. Save changes to keep it on this load.";
    await NotifyChanged();
  }

  private async Task SaveProfileAsync()
  {
    if (!CanEdit || Busy)
      return;
    if (Metadata.BrokerId.HasValue && _selected is null)
    {
      _error = "Search and select this broker before updating shared defaults.";
      return;
    }
    var owner = Metadata;
    var profile = new BrokerProfile
    {
      Id = Metadata.BrokerId ?? _newId,
      Revision = _selected?.Revision ?? 0,
      Name = Metadata.BrokerCompany,
      Contact = Metadata.BrokerContact,
      Phone = Metadata.BrokerPhone,
      Email = Metadata.BrokerEmail,
      BillTo = Metadata.BillTo,
      Terms = Copy(Metadata.PaymentTerms),
    };
    Busy = true;
    _error = _notice = null;
    await BusyChanged.InvokeAsync(true);
    var result = await Api.PutAsync<BrokerProfile, BrokerProfile>(
      "api/brokers",
      profile,
      _lifetime.Token
    );
    if (_disposed)
      return;
    Busy = false;
    await BusyChanged.InvokeAsync(false);
    if (!ReferenceEquals(owner, Metadata))
      return;
    if (!result.Success || result.Response is null)
    {
      _error =
        result.ErrorMessage
        ?? "Save not confirmed. Search the broker before retrying.";
      return;
    }
    _selected = result.Response;
    Metadata.BrokerId = result.Response.Id;
    _notice = "Broker defaults saved. Save changes to update this load.";
    await NotifyChanged();
  }

  private static T Copy<T>(T value) =>
    JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;

  public void Dispose()
  {
    _disposed = true;
    _lifetime.Cancel();
    _lifetime.Dispose();
  }
}
