using System.Net;
using System.Net.Http.Json;
using Client.Services;
using Client.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace Client.Tests.Identity;

[Trait("Category", "Identity")]
[Trait("Kind", "Unit")]
public sealed class CrossTabSessionTests
{
  [Fact]
  public async Task AnOldTabsNewMutationNeverUsesAnotherTabsLogin()
  {
    var js = new LocalStorageJsRuntime();
    await using var oldTab = new TokenStorageService(js);
    await using var newTab = new TokenStorageService(js);
    await oldTab.SetTokensAsync("first", "refresh-first");
    await newTab.SetTokensAsync("second", "refresh-second");
    using var services = new ServiceCollection().BuildServiceProvider();
    var authentication = new AppAuthenticationStateProvider(oldTab, services);
    var notifications = 0;
    authentication.AuthenticationStateChanged += _ => notifications++;
    var calls = 0;
    using var client = new HttpClient(
      new AuthHeaderHandler(oldTab, authentication)
      {
        InnerHandler = new StubHttpMessageHandler(
          (_, _) =>
          {
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
          }
        ),
      }
    );
    var error = await Assert.ThrowsAsync<HttpRequestException>(
      () =>
        client.PostAsJsonAsync(
          "https://example.test/api/data",
          new { value = "first-login-draft" }
        )
    );
    Assert.Contains("another tab", error.Message);
    Assert.Equal(0, calls);
    Assert.Equal(1, notifications);
    Assert.Null(await oldTab.GetSessionAsync());
    Assert.Equal("second", await newTab.GetAccessTokenAsync());
    Assert.False(
      (await authentication.GetAuthenticationStateAsync())
        .User
        .Identity
        ?.IsAuthenticated
    );
  }

  [Fact]
  public async Task SameLoginRotationFromAnotherTabRemainsUsable()
  {
    var js = new LocalStorageJsRuntime();
    await using var firstTab = new TokenStorageService(js);
    await using var secondTab = new TokenStorageService(js);
    await firstTab.SetTokensAsync("original", "refresh-original");
    var original = Assert.IsType<TokenStorageService.Session>(
      await secondTab.GetSessionAsync()
    );
    Assert.True(
      await secondTab.ReplaceAsync(
        original,
        original with
        {
          AccessToken = "rotated",
          RefreshToken = "refresh-rotated",
        }
      )
    );
    using var services = new ServiceCollection().BuildServiceProvider();
    var tokens = new List<string?>();
    using var client = new HttpClient(
      new AuthHeaderHandler(firstTab, new(firstTab, services))
      {
        InnerHandler = new StubHttpMessageHandler(
          (request, _) =>
          {
            tokens.Add(request.Headers.Authorization?.Parameter);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
          }
        ),
      }
    );
    using var response = await client.GetAsync("https://example.test/api/data");
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Equal(new[] { "rotated" }, tokens);
    Assert.Equal(original.Id, (await firstTab.GetSessionAsync())!.Id);
    Assert.False(firstTab.SessionChanged);
  }

  [Fact]
  public async Task AnAnonymousTabRejectsRemoteLoginUntilItsOwnExplicitLogin()
  {
    var js = new LocalStorageJsRuntime();
    await using var oldTab = new TokenStorageService(js);
    await using var otherTab = new TokenStorageService(js);
    Assert.Null(await oldTab.GetSessionAsync());
    await otherTab.SetTokensAsync("remote", "refresh-remote");
    using var services = new ServiceCollection().BuildServiceProvider();
    var requests = new List<(string Path, string? Token)>();
    using var client = new HttpClient(
      new AuthHeaderHandler(oldTab, new(oldTab, services))
      {
        InnerHandler = new StubHttpMessageHandler(
          (request, _) =>
          {
            requests.Add(
              (
                request.RequestUri!.AbsolutePath,
                request.Headers.Authorization?.Parameter
              )
            );
            return Task.FromResult(
              request.RequestUri.AbsolutePath.EndsWith("/login")
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                  Content = JsonContent.Create(
                    new
                    {
                      accessToken = "local",
                      refreshToken = "refresh-local",
                    }
                  ),
                }
                : new HttpResponseMessage(HttpStatusCode.OK)
            );
          }
        ),
      }
    )
    {
      BaseAddress = new("https://example.test/"),
    };
    await Assert.ThrowsAsync<HttpRequestException>(
      () => client.PostAsJsonAsync("api/data", new { value = "old-draft" })
    );
    Assert.Empty(requests);
    await new AuthService(client, oldTab, new(new(client))).LoginAsync(
      "local@example.test",
      "synthetic-password"
    );
    Assert.Equal("local", await oldTab.GetAccessTokenAsync());
    Assert.False(oldTab.SessionChanged);
    using var response = await client.PostAsJsonAsync(
      "api/data",
      new { value = "new-draft" }
    );
    Assert.Equal(
      new[] { ("/api/auth/login", (string?)null), ("/api/data", "local") },
      requests
    );
  }

  [Fact]
  public async Task RemoteLogoutStopsOldTabRequestsAndAReloadCanAdoptANewLogin()
  {
    var js = new LocalStorageJsRuntime();
    await using var oldTab = new TokenStorageService(js);
    await using var otherTab = new TokenStorageService(js);
    await oldTab.SetTokensAsync("first", "refresh-first");
    Assert.NotNull(await otherTab.GetSessionAsync());
    await otherTab.ClearAsync();
    Assert.Null(await oldTab.GetSessionAsync());
    Assert.True(oldTab.SessionChanged);
    await otherTab.SetTokensAsync("second", "refresh-second");
    Assert.Null(await oldTab.GetSessionAsync());
    await using var reloadedTab = new TokenStorageService(js);
    Assert.Equal("second", await reloadedTab.GetAccessTokenAsync());
    Assert.False(reloadedTab.SessionChanged);
    await oldTab.ClearAsync();
    Assert.Equal("second", await otherTab.GetAccessTokenAsync());
  }
}
