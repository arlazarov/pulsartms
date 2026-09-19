using System.Text.Json;
using Microsoft.JSInterop;

namespace Client.Services;

public class TokenStorageService(IJSRuntime jsRuntime) : IAsyncDisposable
{
  private readonly Lazy<Task<IJSObjectReference>> _module = new(
    () =>
      jsRuntime
        .InvokeAsync<IJSObjectReference>(
          "import",
          "./js/generated/shared/authStorage.js"
        )
        .AsTask()
  );
  private readonly SemaphoreSlim _gate = new(1, 1);
  private bool _adopted;
  private Guid? _sessionId;
  internal bool SessionChanged { get; private set; }

  internal sealed record Session(
    Guid Id,
    string AccessToken,
    string RefreshToken
  );

  public async Task SetTokensAsync(string accessToken, string refreshToken)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
    ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);
    await _gate.WaitAsync();
    try
    {
      var session = new Session(Guid.NewGuid(), accessToken, refreshToken);
      await (await _module.Value).InvokeVoidAsync(
        "setSession",
        JsonSerializer.Serialize(session)
      );
      Adopt(session.Id);
    }
    finally
    {
      _gate.Release();
    }
  }

  public async Task<string?> GetAccessTokenAsync() =>
    (await GetSessionAsync())?.AccessToken;

  public async Task<string?> GetRefreshTokenAsync() =>
    (await GetSessionAsync())?.RefreshToken;

  internal async Task<Session?> GetSessionAsync()
  {
    await _gate.WaitAsync();
    try
    {
      var session = await ReadAsync();
      if (!_adopted)
        Adopt(session?.Id);
      // Remote account changes cannot transfer an old tab's forms to a new
      // identity.
      if (session?.Id != _sessionId)
        SessionChanged = true;
      return SessionChanged ? null : session;
    }
    finally
    {
      _gate.Release();
    }
  }

  private async Task<Session?> ReadAsync()
  {
    var json = await (await _module.Value).InvokeAsync<string?>("readSession");
    if (json is null)
      return null;
    try
    {
      var session = JsonSerializer.Deserialize<Session>(json);
      return
        session is { Id: var id }
        && id != Guid.Empty
        && !string.IsNullOrWhiteSpace(session.AccessToken)
        && !string.IsNullOrWhiteSpace(session.RefreshToken)
        ? session
        : null;
    }
    catch (JsonException)
    {
      return null;
    }
  }

  internal async Task<bool> ReplaceAsync(Session expected, Session? replacement)
  {
    await _gate.WaitAsync();
    try
    {
      if (SessionChanged || expected.Id != _sessionId)
        return false;
      var replaced = await (await _module.Value).InvokeAsync<bool>(
        "replaceSession",
        JsonSerializer.Serialize(expected),
        replacement is null ? null : JsonSerializer.Serialize(replacement)
      );
      if (replaced && replacement is null)
        Adopt(null);
      return replaced;
    }
    finally
    {
      _gate.Release();
    }
  }

  internal async Task<bool> ClearSessionAsync(Guid sessionId)
  {
    await _gate.WaitAsync();
    try
    {
      if (SessionChanged || sessionId != _sessionId)
        return false;
      var cleared = await (await _module.Value).InvokeAsync<bool>(
        "clearSession",
        sessionId.ToString()
      );
      if (cleared)
        Adopt(null);
      return cleared;
    }
    finally
    {
      _gate.Release();
    }
  }

  public async Task ClearAsync()
  {
    if (await GetSessionAsync() is { } session)
      await ClearSessionAsync(session.Id);
  }

  private void Adopt(Guid? id)
  {
    _adopted = true;
    _sessionId = id;
    SessionChanged = false;
  }

  public async ValueTask DisposeAsync()
  {
    if (_module.IsValueCreated && _module.Value.IsCompletedSuccessfully)
      await (await _module.Value).DisposeAsync();
  }
}
