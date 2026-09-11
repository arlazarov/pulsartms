using System.Collections.Concurrent;
using Microsoft.JSInterop;

namespace Client.Tests.Support;

internal sealed class MapInteropStub : IJSRuntime, IJSObjectReference
{
  public ConcurrentQueue<(string Name, object?[]? Args)> Calls { get; } = new();
  public Func<string, object?[]?, Task<object?>>? Respond { get; set; }
  public int DisposeCount { get; private set; }

  public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
    InvokeAsync<TValue>(identifier, default, args);

  public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
  {
    cancellationToken.ThrowIfCancellationRequested();
    Calls.Enqueue((identifier, args));
    var result = Respond is null ? null : await Respond(identifier, args);
    return result is null ? default! : (TValue)result;
  }

  public ValueTask DisposeAsync() { DisposeCount++; return ValueTask.CompletedTask; }
}
