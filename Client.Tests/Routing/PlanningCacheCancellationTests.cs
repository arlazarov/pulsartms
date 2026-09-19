using System.Net;
using System.Net.Http.Json;
using Client.Services;
using Client.Tests.Support;

namespace Client.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Unit")]
public sealed class PlanningCacheCancellationTests
{
  [Theory]
  [InlineData("preview")]
  [InlineData("preload")]
  [InlineData("refresh")]
  public async Task ClearStopsTransportAndAllowsFreshSessionReads(string kind)
  {
    var tokens = new List<CancellationToken>();
    var replies = new List<TaskCompletionSource<HttpResponseMessage>>();
    using var handler = new StubHttpMessageHandler(
      (_, ct) =>
      {
        tokens.Add(ct);
        var reply = new TaskCompletionSource<HttpResponseMessage>(
          TaskCreationOptions.RunContinuationsAsynchronously
        );
        replies.Add(reply);
        return reply.Task.WaitAsync(ct);
      }
    );
    using var client = new HttpClient(handler)
    {
      BaseAddress = new("https://fixture.invalid/"),
    };
    using var cache = new PlanningDisplayCache(new ApiService(client));
    var truck = Guid.NewGuid();
    Task Read() =>
      kind switch
      {
        "preview" => cache.ReadPreviewAsync(truck, default),
        "preload" => cache.PreloadAsync(),
        _ => cache.RefreshAsync("planning", default),
      };

    var abandoned = Read();
    cache.Clear();
    Assert.True(tokens[0].IsCancellationRequested);
    await abandoned;

    var current = Read();
    Assert.Equal(2, tokens.Count);
    Assert.False(tokens[1].IsCancellationRequested);
    replies[1]
      .SetResult(
        new(HttpStatusCode.OK)
        {
          Content = JsonContent.Create(new { success = true }),
        }
      );
    await current;
  }

  [Fact]
  public async Task DisposeAlsoCancelsSupersededRefreshes()
  {
    var tokens = new List<CancellationToken>();
    using var handler = new StubHttpMessageHandler(
      (_, ct) =>
      {
        tokens.Add(ct);
        var reply = new TaskCompletionSource<HttpResponseMessage>(
          TaskCreationOptions.RunContinuationsAsynchronously
        );
        return reply.Task.WaitAsync(ct);
      }
    );
    using var client = new HttpClient(handler)
    {
      BaseAddress = new("https://fixture.invalid/"),
    };
    using var cache = new PlanningDisplayCache(new ApiService(client));
    var first = cache.RefreshAsync("planning", default);
    var replacement = cache.RefreshAsync("planning", default, force: true);

    cache.Dispose();

    Assert.Equal(2, tokens.Count);
    Assert.All(tokens, token => Assert.True(token.IsCancellationRequested));
    await Task.WhenAll(first, replacement);
    Assert.Null(cache.Get("planning"));
  }
}
