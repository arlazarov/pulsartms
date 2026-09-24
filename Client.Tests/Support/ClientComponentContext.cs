using Bunit;
using Bunit.TestDoubles;
using Client.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.JSInterop;

namespace Client.Tests.Support;

internal sealed class ClientComponentContext : BunitContext
{
  public PageVisibilityInterop Visibility { get; } = new();

  // Who is signed in, for a test that needs a named dispatcher.
  public BunitAuthorizationContext Authorization { get; }

  public ClientComponentContext(
    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send
  )
  {
    Authorization = this.AddAuthorization();
    Visibility.Configure(JSInterop);
    Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
    Services.AddSingleton(_ => new HttpClient(new StubHttpMessageHandler(send))
    {
      BaseAddress = new("http://localhost/"),
    });
    Services.AddSingleton<ApiService>();
    Services.AddSingleton<PlanningDisplayCache>();
    Services.AddSingleton(TimeProvider.System);
    // As in the app: the navigation's unread notice is on every page.
    Services.TryAddSingleton<TokenStorageService>();
    Services.AddSingleton<MessagingSignals>();
    Services.AddSingleton<MessagingNotices>();
  }

  public void AddAuthenticationServices()
  {
    Services.AddSingleton<IJSRuntime, LocalStorageJsRuntime>();
    Services.AddSingleton<TokenStorageService>();
    Services.AddSingleton<AuthService>();
    Services.AddSingleton<AppAuthenticationStateProvider>();
  }
}
