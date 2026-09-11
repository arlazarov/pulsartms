using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Client.Services;
using Client.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Client.Tests.Identity;

[Trait("Category", "Identity")]
[Trait("Kind", "Unit")]
public sealed class SessionLifecycleTests
{
  [Fact]
  public async Task SessionBoundLogoutNeverReachesTheServerUnderANewerLogin()
  {
    await using var storage = new TokenStorageService(new LocalStorageJsRuntime());
    await storage.SetTokensAsync("first", "refresh-first");
    var first = Assert.IsType<TokenStorageService.Session>(await storage.GetSessionAsync());
    await storage.SetTokensAsync("second", "refresh-second");
    using var services = new ServiceCollection().BuildServiceProvider();
    var calls = 0;
    using var client = new HttpClient(new AuthHeaderHandler(storage, new(storage, services))
    {
      InnerHandler = new StubHttpMessageHandler((_, _) =>
      {
        calls++;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
      })
    });
    using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.test/api/auth/logout");
    request.Options.Set(AuthHeaderHandler.RequiredSession, first.Id);
    using var response = await client.SendAsync(request);
    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    Assert.Equal(0, calls);
    Assert.Equal("second", await storage.GetAccessTokenAsync());
  }

  [Fact]
  public async Task LogoutClearsTheSameLoginAfterItsExpiredAccessTokenIsRefreshed()
  {
    await using var storage = new TokenStorageService(new LocalStorageJsRuntime());
    await storage.SetTokensAsync("old", "refresh");
    using var services = new ServiceCollection().BuildServiceProvider();
    var refreshes = 0;
    using var client = new HttpClient(new AuthHeaderHandler(storage, new(storage, services))
    {
      InnerHandler = new StubHttpMessageHandler((request, _) =>
      {
        if (request.RequestUri!.AbsolutePath.EndsWith("/refresh"))
        {
          refreshes++;
          return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = JsonContent.Create(new { accessToken = "rotated", refreshToken = "rotated-refresh" }) });
        }
        return Task.FromResult(new HttpResponseMessage(request.Headers.Authorization?.Parameter == "rotated"
          ? HttpStatusCode.OK : HttpStatusCode.Unauthorized));
      })
    }) { BaseAddress = new("https://example.test/") };
    await new AuthService(client, storage, new(new(client))).LogoutAsync();
    Assert.Equal(1, refreshes);
    Assert.Null(await storage.GetSessionAsync());
  }

  [Theory]
  [InlineData(HttpStatusCode.OK)]
  [InlineData(HttpStatusCode.ServiceUnavailable)]
  public async Task DelayedLogoutNeverClearsAnotherLogin(HttpStatusCode status)
  {
    var js = new LocalStorageJsRuntime();
    await using var firstTab = new TokenStorageService(js);
    await using var secondTab = new TokenStorageService(js);
    await firstTab.SetTokensAsync("first", "refresh-first");
    var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    using var client = new HttpClient(new StubHttpMessageHandler(async (_, ct) =>
    {
      started.SetResult();
      await release.Task.WaitAsync(ct);
      return new(status);
    })) { BaseAddress = new("https://example.test/") };
    var pending = new AuthService(client, firstTab, new(new(client))).LogoutAsync();
    await started.Task;
    await secondTab.SetTokensAsync("second", "refresh-second");
    release.SetResult();
    if (status == HttpStatusCode.OK) await pending;
    else await Assert.ThrowsAsync<HttpRequestException>(() => pending);
    Assert.Null(await firstTab.GetAccessTokenAsync());
    Assert.True(firstTab.SessionChanged);
    Assert.Equal("refresh-second", await secondTab.GetRefreshTokenAsync());
  }

  [Theory]
  [InlineData("{")]
  [InlineData("{}")]
  [InlineData("null")]
  [InlineData("{\"Id\":\"invalid\",\"AccessToken\":\"a\",\"RefreshToken\":\"r\"}")]
  public async Task InvalidStoredEnvelopesAreAnonymous(string json)
  {
    var js = new LocalStorageJsRuntime();
    await js.InvokeVoidAsync("localStorage.setItem", "auth_session", json);
    await using var storage = new TokenStorageService(js);
    Assert.Null(await storage.GetSessionAsync());
    using var services = new ServiceCollection().BuildServiceProvider();
    Assert.False((await new AppAuthenticationStateProvider(storage, services).GetAuthenticationStateAsync()).User.Identity?.IsAuthenticated);
  }

  [Fact]
  public async Task MalformedRefreshTokensDoNotReplaceAValidSession()
  {
    await using var storage = new TokenStorageService(new LocalStorageJsRuntime());
    await storage.SetTokensAsync("old", "refresh");
    using var services = new ServiceCollection().BuildServiceProvider();
    using var client = new HttpClient(new AuthHeaderHandler(storage, new(storage, services))
    {
      InnerHandler = new StubHttpMessageHandler((request, _) => Task.FromResult(
        request.RequestUri!.AbsolutePath.EndsWith("/refresh")
          ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { accessToken = "", refreshToken = "" }) }
          : new HttpResponseMessage(HttpStatusCode.Unauthorized)))
    });
    await Assert.ThrowsAsync<JsonException>(() => client.GetAsync("https://example.test/api/data"));
    Assert.Equal("old", await storage.GetAccessTokenAsync());
    Assert.Equal("refresh", await storage.GetRefreshTokenAsync());
  }
}
