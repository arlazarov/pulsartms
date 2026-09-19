using Bunit;
using Client.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Client.Tests.Support;

internal sealed class ClientComponentContext : BunitContext
{
  public PageVisibilityInterop Visibility { get; } = new();

  public ClientComponentContext(
    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send
  )
  {
    this.AddAuthorization();
    Visibility.Configure(JSInterop);
    Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
    Services.AddSingleton(_ => new HttpClient(new StubHttpMessageHandler(send))
    {
      BaseAddress = new("http://localhost/"),
    });
    Services.AddSingleton<ApiService>();
    Services.AddSingleton<PlanningDisplayCache>();
    Services.AddSingleton(TimeProvider.System);
  }

  public void AddAuthenticationServices()
  {
    Services.AddSingleton<IJSRuntime, LocalStorageJsRuntime>();
    Services.AddSingleton<TokenStorageService>();
    Services.AddSingleton<AuthService>();
    Services.AddSingleton<AppAuthenticationStateProvider>();
  }
}
