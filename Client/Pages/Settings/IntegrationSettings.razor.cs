using System.Net;
using Client.Models.DTO.Integrations;
using Client.Services;
using Microsoft.AspNetCore.Components;

namespace Client.Pages.Settings;

public partial class IntegrationSettings : IDisposable
{
  [Inject]
  private ApiService Api { get; set; } = default!;
  private readonly CancellationTokenSource _lifetime = new();
  private readonly IntegrationDraft[] _cards =
  [
    new(
      "torqueai",
      "TorqueAI",
      "Dispatch and load synchronization.",
      [new("apiKey", "API key")]
    ),
    new(
      "samsara",
      "Samsara",
      "Fleet locations, fuel levels and driver hours.",
      [new("apiKey", "API key")]
    ),
    new(
      "google-email",
      "Google for Emails",
      "Google credentials for email integrations.",
      [
        new("clientId", "Client ID"),
        new("clientSecret", "Client secret"),
        new("refreshToken", "Refresh token"),
      ]
    ),
    new(
      "whatsapp",
      "WhatsApp",
      "Fuel plans to drivers through the WhatsApp Cloud API. Saving sends "
        + "nothing.",
      [
        new("phoneNumberId", "Phone number ID"),
        new("accessToken", "Access token"),
        new("appSecret", "App secret"),
        new("verifyToken", "Webhook verify token"),
      ]
    ),
  ];
  private string? _webhook;
  private bool _loading,
    _disposed;
  private string? _loadError;

  protected override Task OnInitializedAsync() => LoadAsync();

  private async Task LoadAsync()
  {
    if (_loading || _disposed)
      return;
    _loading = true;
    _loadError = null;
    var result = await Api.GetAsync<IReadOnlyList<IntegrationConnectionState>>(
      "api/settings/integrations",
      _lifetime.Token
    );
    if (_disposed)
      return;
    _loading = false;
    if (!result.Success || result.Response is null)
    {
      _loadError = Error(
        result.HttpStatusCode,
        "Integrations could not be loaded. Try again."
      );
      return;
    }
    foreach (var card in _cards)
    {
      if (card.Editing || card.Busy || card.ConfirmRestore)
        continue;
      card.State = FindState(result.Response, card.Provider);
    }
    if (_cards.Any(card => card.State is null))
      _loadError =
        "Some integration settings are unavailable. Reload to try again.";
    var webhook = await Api.GetAsync<WhatsAppWebhookAddress>(
      "api/settings/integrations/whatsapp/webhook",
      _lifetime.Token
    );
    if (!_disposed && webhook.Success && webhook.Response is { } address)
      _webhook = Api.BaseAddress is { } api
        ? new Uri(api, address.Path).ToString()
        : "/" + address.Path;
  }

  private async Task RefreshAsync(IntegrationDraft card)
  {
    if (card.Busy || _disposed)
      return;
    card.Busy = true;
    var result = await Api.GetAsync<IReadOnlyList<IntegrationConnectionState>>(
      "api/settings/integrations",
      _lifetime.Token
    );
    if (_disposed)
      return;
    card.Busy = false;
    var state =
      result.Success && result.Response is not null
        ? FindState(result.Response, card.Provider)
        : null;
    if (state is null)
    {
      card.Error = Error(
        result.HttpStatusCode,
        "Status could not be refreshed. Your entries are unchanged."
      );
      return;
    }
    card.State = state;
    card.NeedsRefresh = false;
    card.Error = null;
    if (!state.CanRestoreDeployment)
      card.ConfirmRestore = false;
  }

  private void Edit(IntegrationDraft card)
  {
    if (card.Busy || card.State is null || _disposed)
      return;
    Cancel(card);
    card.Editing = true;
  }

  private void RequestRestore(IntegrationDraft card)
  {
    if (card.Busy || card.State?.CanRestoreDeployment != true || _disposed)
      return;
    Cancel(card);
    card.ConfirmRestore = true;
  }

  private void Cancel(IntegrationDraft card)
  {
    if (card.Busy || _disposed)
      return;
    card.ClearValues();
    card.Editing = card.ConfirmRestore = card.Saved = false;
    card.Error = card.NeedsRefresh ? Error(HttpStatusCode.Conflict, "") : null;
  }

  private void Change(IntegrationDraft card, string name, string? value)
  {
    if (card.Busy || !card.Editing || _disposed)
      return;
    card.Values[name] = value ?? "";
    card.Saved = false;
  }

  private async Task SaveAsync(IntegrationDraft card, bool restore)
  {
    if (card.Busy || _disposed || card.State is null || card.NeedsRefresh)
      return;
    if (
      restore
        ? !card.ConfirmRestore || !card.State.CanRestoreDeployment
        : !CanSave(card)
    )
      return;
    card.Busy = true;
    card.Saved = false;
    card.Error = null;
    var request = new IntegrationCredentialsUpdate
    {
      Revision = card.State.Revision,
      RestoreDeployment = restore,
    };
    if (!restore)
      foreach (var (name, value) in card.Values)
        if (!string.IsNullOrWhiteSpace(value))
          request.Fields[name] = value;
    try
    {
      var result = await Api.PutAsync<
        IntegrationCredentialsUpdate,
        IntegrationConnectionState
      >($"api/settings/integrations/{card.Provider}", request, _lifetime.Token);
      if (_disposed)
        return;
      if (!result.Success || result.Response?.Provider != card.Provider)
      {
        card.NeedsRefresh = result.HttpStatusCode == HttpStatusCode.Conflict;
        card.Error = Error(
          result.HttpStatusCode,
          "Could not save this integration. Your entries are unchanged; try again."
        );
        return;
      }
      card.State = result.Response;
      card.ClearValues();
      card.Editing = card.ConfirmRestore = card.NeedsRefresh = false;
      card.Saved = true;
    }
    finally
    {
      request.Fields.Clear();
      if (!_disposed)
        card.Busy = false;
    }
  }

  private static bool CanSave(IntegrationDraft card) =>
    card.Editing
    && !card.Busy
    && !card.NeedsRefresh
    && card.Values.Values.Any(value => !string.IsNullOrWhiteSpace(value));

  private static string Status(IntegrationDraft card) =>
    card.State is null ? "Unavailable"
    : card.State.Configured ? "Configured"
    : "Not configured";

  private static bool FieldConfigured(IntegrationDraft card, string name) =>
    card.State?.Fields.Any(field => field.Name == name && field.Configured)
    == true;

  private static string FieldId(IntegrationDraft card, CredentialField field) =>
    $"integration-{card.Provider}-{field.Name}";

  private static IntegrationConnectionState? FindState(
    IReadOnlyList<IntegrationConnectionState> states,
    string provider
  )
  {
    var matches = states
      .Where(state => state.Provider == provider)
      .Take(2)
      .ToArray();
    return matches.Length == 1 ? matches[0] : null;
  }

  private static string Error(HttpStatusCode? status, string fallback) =>
    status switch
    {
      HttpStatusCode.Unauthorized => "Your session has expired. Sign in again.",
      HttpStatusCode.Forbidden =>
        "Only administrators can manage integrations.",
      HttpStatusCode.Conflict =>
        "Settings changed elsewhere. Refresh status before saving again.",
      HttpStatusCode.BadRequest => "Check the credential fields and try again.",
      _ => fallback,
    };

  public void Dispose()
  {
    if (_disposed)
      return;
    _disposed = true;
    foreach (var card in _cards)
      card.ClearValues();
    _lifetime.Cancel();
    _lifetime.Dispose();
  }

  private sealed record CredentialField(string Name, string Label);

  private sealed class IntegrationDraft(
    string provider,
    string title,
    string description,
    CredentialField[] fields
  )
  {
    public string Provider { get; } = provider;
    public string Title { get; } = title;
    public string Description { get; } = description;
    public CredentialField[] Fields { get; } = fields;
    public Dictionary<string, string> Values { get; } =
      fields.ToDictionary(field => field.Name, _ => "", StringComparer.Ordinal);
    public IntegrationConnectionState? State { get; set; }
    public bool Editing,
      ConfirmRestore,
      Busy,
      Saved,
      NeedsRefresh;
    public string? Error;

    public void ClearValues()
    {
      foreach (var name in Values.Keys)
        Values[name] = "";
    }
  }

  // Where the credentials in use come from. A connection with nothing saved
  // and no server configuration - WhatsApp has none - has neither.
  private static string Source(IntegrationConnectionState state) =>
    state.UsesSavedSettings ? "Saved in Settings"
    : state.Configured ? "Server configuration"
    : "Nothing saved";
}
