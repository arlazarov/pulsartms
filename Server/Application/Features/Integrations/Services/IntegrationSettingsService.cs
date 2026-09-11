using Application.Features.Integrations.Interfaces;
using Application.Features.Integrations.Models;
using Application.Models;

namespace Application.Features.Integrations.Services;

public sealed class IntegrationSettingsService(IIntegrationCredentialStore store, IIntegrationDeploymentCredentials deployment)
  : IIntegrationCredentials
{
  public async Task<IntegrationCredentialValues> GetAsync(string provider, CancellationToken ct)
  {
    if (!IntegrationProviderCatalog.Contains(provider)) throw new ArgumentException("Unsupported integration.", nameof(provider));
    var saved = await store.ReadAsync(provider, ct);
    return saved.Values ?? deployment.Get(provider);
  }

  public async Task<IntegrationConnectionState> GetStateAsync(string provider, CancellationToken ct) =>
    State(provider, await store.ReadAsync(provider, ct), deployment.Get(provider));

  public async Task<RequestResponse<IntegrationConnectionState>> SaveAsync(string provider, IntegrationCredentialsUpdate update, CancellationToken ct)
  {
    var saved = await store.ReadAsync(provider, ct);
    if (saved.Revision != update.Revision) return Conflict();
    var defaults = deployment.Get(provider);
    var effective = saved.Values ?? defaults;
    IntegrationCredentialValues? replacement;
    if (update.RestoreDeployment)
    {
      if (!IntegrationProviderCatalog.IsConfigured(provider, defaults))
        return RequestResponse<IntegrationConnectionState>.Fail("The server configuration is incomplete. Existing settings were kept.");
      replacement = null;
    }
    else
    {
      var changed = update.Fields!.Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
        .ToDictionary(pair => pair.Key, pair => pair.Value!.Trim(), StringComparer.Ordinal);
      if (changed.Count == 0) return RequestResponse<IntegrationConnectionState>.Ok(State(provider, saved, defaults));
      if (provider == IntegrationProviderCatalog.GoogleEmail && changed.TryGetValue("clientId", out var clientId)
        && clientId != effective.Get("clientId")
        && (!changed.ContainsKey("clientSecret") || !changed.ContainsKey("refreshToken")))
        return RequestResponse<IntegrationConnectionState>.Fail("Changing the Google Client ID requires its Client secret and Refresh token together. Existing settings were kept.");
      replacement = new(IntegrationProviderCatalog.Fields(provider).Select(field =>
        new KeyValuePair<string, string>(field, changed.GetValueOrDefault(field) ?? effective.Get(field) ?? string.Empty)));
      if (!IntegrationProviderCatalog.IsConfigured(provider, replacement))
        return RequestResponse<IntegrationConnectionState>.Fail("Complete the missing credentials before saving. Existing settings were kept.");
      if (saved.Values is not null && IntegrationProviderCatalog.Fields(provider).All(field => replacement.Get(field) == saved.Values.Get(field)))
        return RequestResponse<IntegrationConnectionState>.Ok(State(provider, saved, defaults));
    }
    if (!await store.TryWriteAsync(provider, update.Revision, replacement, ct)) return Conflict();
    return RequestResponse<IntegrationConnectionState>.Ok(await GetStateAsync(provider, ct));
  }

  private static IntegrationConnectionState State(string provider, StoredIntegrationCredentials saved, IntegrationCredentialValues defaults)
  {
    var effective = saved.Values ?? defaults;
    return new(provider, IntegrationProviderCatalog.IsConfigured(provider, effective), saved.Values is not null,
      saved.Values is not null && IntegrationProviderCatalog.IsConfigured(provider, defaults), saved.Revision, saved.UpdatedAt,
      IntegrationProviderCatalog.Fields(provider).Select(field => new IntegrationCredentialFieldState(field,
        !string.IsNullOrWhiteSpace(effective.Get(field)))).ToArray());
  }

  private static RequestResponse<IntegrationConnectionState> Conflict() => RequestResponse<IntegrationConnectionState>.Fail(
    "This connection was changed in another session. Reload it before saving.", 409);
}
