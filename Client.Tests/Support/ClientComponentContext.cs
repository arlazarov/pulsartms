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
    Services.AddSingleton<IPageVisibility, AlwaysVisible>();
  }

  internal sealed class AlwaysVisible : IPageVisibility
  {
    public bool Hidden => false;
    public event Action? Changed { add { } remove { } }
    public Task StartAsync() => Task.CompletedTask;
  }

  public void AddAuthenticationServices()
  {
    Services.AddSingleton<IJSRuntime, LocalStorageJsRuntime>();
    Services.AddSingleton<TokenStorageService>();
    Services.AddSingleton<AuthService>();
    Services.AddSingleton<AppAuthenticationStateProvider>();
  }
}
