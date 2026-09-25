using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Bunit;
using Bunit.TestDoubles;
using Client.Models.DTO;
using Client.Models.DTO.DriverGroups;
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

  // Pages that open loads write their place into their address; a test
  // that checks it reads this module's calls.
  public BunitJSModuleInterop ReturnPlace { get; }

  public ClientComponentContext(
    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send
  )
  {
    Authorization = this.AddAuthorization();
    Visibility.Configure(JSInterop);
    ReturnPlace = JSInterop.SetupModule("./js/generated/shared/returnPlace.js");
    ReturnPlace.Mode = JSRuntimeMode.Loose;
    Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
    Services.AddSingleton(_ => new HttpClient(
      new StubHttpMessageHandler(
        async (request, ct) =>
        {
          if (TakeHold(request.RequestUri?.AbsolutePath) is { } held)
            await held.HoldAsync();
          return await SendAsync(send, request, ct);
        }
      )
    )
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
    Services.AddSingleton<ChosenDriverGroup>();
    Services.AddSingleton<ReturnPlaces>();
  }

  private readonly ConcurrentDictionary<string, HeldRequest> _holds = new();

  // The next request to this path is held until released (HeldRequest).
  public HeldRequest Hold(string path) => _holds[path] = new HeldRequest();

  private HeldRequest? TakeHold(string? path)
  {
    if (path is null || !_holds.TryRemove(path, out var held))
      return null;
    _taken.Enqueue(held);
    return held;
  }

  private readonly ConcurrentQueue<HeldRequest> _taken = new();

  // A request still held when the test ends is answered, so nothing is
  // left waiting after the page is gone.
  protected override ValueTask DisposeAsyncCore()
  {
    foreach (var held in _taken)
      held.Answer();
    return base.DisposeAsyncCore();
  }

  // Every page that lists drivers shows the driver group picker. A test
  // that does not answer for groups gets none chosen and none made, as a
  // dispatcher who never made one.
  private static async Task<HttpResponseMessage> SendAsync(
    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send,
    HttpRequestMessage request,
    CancellationToken ct
  )
  {
    var response = await send(request, ct);
    return
      response.StatusCode == HttpStatusCode.NotFound
      && request.Method == HttpMethod.Get
      && request.RequestUri?.AbsolutePath == "/api/driver-groups"
      ? new(HttpStatusCode.OK)
      {
        Content = JsonContent.Create(
          new RequestResponseDTO<DriverGroupsView>
          {
            Success = true,
            Response = new(null, []),
          }
        ),
      }
      : response;
  }

  public void AddAuthenticationServices()
  {
    Services.AddSingleton<IJSRuntime, LocalStorageJsRuntime>();
    Services.AddSingleton<TokenStorageService>();
    Services.AddSingleton<AuthService>();
    Services.AddSingleton<AppAuthenticationStateProvider>();
  }
}
