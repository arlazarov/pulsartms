using System.Net;
using System.Net.Http.Json;
using Client.Services;
using Client.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Client.Tests.Identity;

[Trait("Category", "Identity")]
[Trait("Kind", "Unit")]
public class ClientRefreshTests
{
  [Theory]
  [InlineData(HttpStatusCode.OK, 1, "new")]
  [InlineData(HttpStatusCode.Unauthorized, 1, null)]
  public async Task ConcurrentUnauthorizedRequestsShareRefresh(HttpStatusCode status, int calls, string? expected)
  {
    var js = new LocalStorageJsRuntime();
    var storage = new TokenStorageService(js);
    await storage.SetTokensAsync("old", "refresh");
    using var services = new ServiceCollection().BuildServiceProvider();
    var transport = new Transport(status);
    using var client = new HttpClient(new AuthHeaderHandler(storage, new(storage, services)) { InnerHandler = transport });
    var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => client.GetAsync("https://example.test/api/data")));
    foreach (var response in responses) response.Dispose();
    Assert.Equal(calls, transport.RefreshCalls);
    Assert.Equal(expected, await storage.GetAccessTokenAsync());
  }

  [Fact]
  public async Task TemporaryRefreshFailureKeepsTokens()
  {
    var storage = new TokenStorageService(new LocalStorageJsRuntime());
    await storage.SetTokensAsync("old", "refresh");
    using var services = new ServiceCollection().BuildServiceProvider();
    using var client = new HttpClient(new AuthHeaderHandler(storage, new(storage, services)) { InnerHandler = new Transport(HttpStatusCode.ServiceUnavailable) });
    await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("https://example.test/api/data"));
    Assert.Equal("old", await storage.GetAccessTokenAsync());
    Assert.Equal("refresh", await storage.GetRefreshTokenAsync());
  }

  private sealed class Transport(HttpStatusCode status) : HttpMessageHandler
  {
    public int RefreshCalls;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
      if (request.RequestUri!.AbsolutePath == "/api/auth/refresh")
      {
        Interlocked.Increment(ref RefreshCalls);
        await Task.Delay(25, ct);
        return new(status) { Content = JsonContent.Create(new { accessToken = "new", refreshToken = "new-refresh" }) };
      }
      return new(request.Headers.Authorization?.Parameter == "new" ? HttpStatusCode.OK : HttpStatusCode.Unauthorized);
    }
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task ARequestFromAnOldLoginIsNeverReplayedWithAnotherUsersToken(bool switchDuringRefresh)
  {
    var storage = new TokenStorageService(new LocalStorageJsRuntime());
    await storage.SetTokensAsync("account-a", "refresh-a");
    using var services = new ServiceCollection().BuildServiceProvider();
    var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var transport = new SwitchTransport(switchDuringRefresh, started, release);
    using var client = new HttpClient(new AuthHeaderHandler(storage, new(storage, services)) { InnerHandler = transport });
    var pending = client.PostAsync("https://example.test/api/data", JsonContent.Create(new { value = "account-a-update" }));
    await started.Task;
    await storage.SetTokensAsync("account-b", "refresh-b");
    release.SetResult();
    using var response = await pending;
    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    Assert.Equal(new[] { "account-a" }, transport.ResourceTokens);
    Assert.Equal("account-b", await storage.GetAccessTokenAsync());
    Assert.Equal("refresh-b", await storage.GetRefreshTokenAsync());
  }

  [Fact]
  public async Task LegacyTokensAreUpgradedTogetherAndOnlyLoginChangesSessionIdentity()
  {
    var js = new LocalStorageJsRuntime();
    await js.InvokeVoidAsync("localStorage.setItem", "access_token", "legacy-access");
    await js.InvokeVoidAsync("localStorage.setItem", "refresh_token", "legacy-refresh");
    var storage = new TokenStorageService(js);
    var original = Assert.IsType<TokenStorageService.Session>(await storage.GetSessionAsync());
    Assert.Equal(original, await storage.GetSessionAsync());
    Assert.Null(await js.InvokeAsync<string?>("localStorage.getItem", "access_token"));
    var rotated = original with { AccessToken = "rotated", RefreshToken = "rotated-refresh" };
    Assert.True(await storage.ReplaceAsync(original, rotated));
    Assert.Equal(original.Id, (await storage.GetSessionAsync())!.Id);
    await storage.SetTokensAsync("new-login", "new-refresh");
    Assert.NotEqual(original.Id, (await storage.GetSessionAsync())!.Id);
    Assert.False(await storage.ReplaceAsync(rotated, null));
  }

  private sealed class SwitchTransport(bool switchDuringRefresh, TaskCompletionSource started,
    TaskCompletionSource release) : HttpMessageHandler
  {
    public List<string?> ResourceTokens { get; } = [];
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
      var refreshing = request.RequestUri!.AbsolutePath == "/api/auth/refresh";
      if (!refreshing) ResourceTokens.Add(request.Headers.Authorization?.Parameter);
      if (refreshing == switchDuringRefresh)
      {
        started.SetResult();
        await release.Task.WaitAsync(ct);
      }
      return refreshing
        ? new(HttpStatusCode.OK) { Content = JsonContent.Create(new { accessToken = "rotated-a", refreshToken = "rotated-refresh-a" }) }
        : new(HttpStatusCode.Unauthorized);
    }
  }

}
