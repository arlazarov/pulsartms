using Microsoft.JSInterop;
using System.Text.Json;
using Client.Services;

namespace Client.Tests.Support;

internal sealed class LocalStorageJsRuntime : IJSRuntime
{
  private readonly Dictionary<string, string> values = [];

  public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
    InvokeAsync<TValue>(identifier, default, args);

  public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
  {
    cancellationToken.ThrowIfCancellationRequested();
    lock (values)
    {
      if (identifier == "import")
      {
        if (!Equals(args![0], "./js/generated/shared/authStorage.js")) throw new InvalidOperationException("Unexpected module import.");
        return ValueTask.FromResult((TValue)(object)new Module(this));
      }
      if (identifier == "readSession") return Result<TValue>(ReadSession());
      if (identifier == "setSession") WriteSession((string)args![0]!);
      else if (identifier == "replaceSession")
      {
        var current = Parse(ReadSession());
        var expected = Parse((string)args![0]!);
        if (current != expected || expected is null) return Result<TValue>(false);
        WriteSession((string?)args[1]);
        return Result<TValue>(true);
      }
      else if (identifier == "clearSession")
      {
        if (Parse(ReadSession())?.Id.ToString() != (string)args![0]!) return Result<TValue>(false);
        WriteSession(null);
        return Result<TValue>(true);
      }
      else if (identifier == "clear") WriteSession(null);
      else
      {
      var key = (string)args![0]!;
      switch (identifier)
      {
        case "localStorage.setItem": values[key] = (string)args[1]!; break;
        case "localStorage.removeItem": values.Remove(key); break;
        case "localStorage.getItem": return ValueTask.FromResult((TValue)(object?)values.GetValueOrDefault(key)!);
        default: throw new InvalidOperationException($"Unexpected JS call: {identifier}");
      }
      }
      return ValueTask.FromResult(default(TValue)!);
    }
  }

  private static ValueTask<T> Result<T>(object? value) => ValueTask.FromResult((T)value!);

  private string? ReadSession()
  {
    if (values.TryGetValue("auth_session", out var json)) return json;
    var access = values.GetValueOrDefault("access_token");
    var refresh = values.GetValueOrDefault("refresh_token");
    if (string.IsNullOrWhiteSpace(access) || string.IsNullOrWhiteSpace(refresh)) return null;
    json = JsonSerializer.Serialize(new TokenStorageService.Session(Guid.NewGuid(), access, refresh));
    WriteSession(json);
    return json;
  }

  private void WriteSession(string? json)
  {
    values["auth_session"] = json ?? "null";
    values.Remove("access_token");
    values.Remove("refresh_token");
  }

  private static TokenStorageService.Session? Parse(string? json)
  {
    try { return json is null ? null : JsonSerializer.Deserialize<TokenStorageService.Session>(json); }
    catch (JsonException) { return null; }
  }

  private sealed class Module(LocalStorageJsRuntime runtime) : IJSObjectReference
  {
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => runtime.InvokeAsync<TValue>(identifier, args);
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
      runtime.InvokeAsync<TValue>(identifier, cancellationToken, args);
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
  }
}
