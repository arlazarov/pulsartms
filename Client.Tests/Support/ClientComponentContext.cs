using Bunit;
using Client.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Client.Tests.Support;

internal sealed class ClientComponentContext : BunitContext
{
  public ClientComponentContext(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
  {
    Services.AddSingleton(_ => new HttpClient(new StubHttpMessageHandler(send)) { BaseAddress = new("http://localhost/") });
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
