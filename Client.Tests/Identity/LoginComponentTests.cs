using System.Net;
using System.Net.Http.Json;
using Bunit;
using Client.Pages.Auth;
using Client.Services;
using Client.Shared.Brand.BrandLogo;
using Client.Tests.Support;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Client.Tests.Identity;

[Trait("Category", "Identity")]
[Trait("Kind", "Component")]
public sealed class LoginComponentTests
{
  [Fact]
  public async Task SignInFormShowsTheProductBrand()
  {
    await using var context = new ClientComponentContext(
      (_, _) =>
        throw new InvalidOperationException(
          "An empty session must not fetch a profile."
        )
    );
    context.AddAuthenticationServices();
    var component = context.Render<Login>();

    var logo = component.FindComponent<BrandLogo>();
    Assert.True(logo.Instance.Prominent);
    Assert.False(logo.Instance.Reversed);
    Assert.Equal(
      "PulsR TMS",
      component
        .Find(".login-page__brand svg[role='img']")
        .GetAttribute("aria-label")
    );
    Assert.Single(component.FindAll("form"));
  }

  [Fact]
  public async Task AuthenticatedVisitorGoesDirectlyToFleetMapWithoutShowingTheForm()
  {
    await using var context = new ClientComponentContext(
      (request, _) =>
      {
        Assert.Equal("/api/auth/me", request.RequestUri!.AbsolutePath);
        return Task.FromResult(Profile());
      }
    );
    context.AddAuthenticationServices();
    await context
      .Services.GetRequiredService<TokenStorageService>()
      .SetTokensAsync("access", "refresh");
    var navigation = context.Services.GetRequiredService<NavigationManager>();
    navigation.NavigateTo("/login");
    var component = context.Render<Login>();
    component.WaitForAssertion(
      () => Assert.EndsWith("/fleet/map", navigation.Uri)
    );
    Assert.Empty(component.FindAll("form"));
  }

  [Theory]
  [InlineData(HttpStatusCode.Unauthorized)]
  [InlineData(HttpStatusCode.ServiceUnavailable)]
  public async Task UnverifiedSessionKeepsTheLoginForm(HttpStatusCode status)
  {
    await using var context = new ClientComponentContext(
      (_, _) => Task.FromResult(new HttpResponseMessage(status))
    );
    context.AddAuthenticationServices();
    await context
      .Services.GetRequiredService<TokenStorageService>()
      .SetTokensAsync("access", "refresh");
    var navigation = context.Services.GetRequiredService<NavigationManager>();
    navigation.NavigateTo("/login");
    var component = context.Render<Login>();
    component.WaitForAssertion(() => Assert.Single(component.FindAll("form")));
    Assert.EndsWith("/login", navigation.Uri);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task DelayedSessionCheckCannotRedirectAfterLeavingLogin(
    bool dispose
  )
  {
    var release = new TaskCompletionSource<HttpResponseMessage>(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    await using var context = new ClientComponentContext(
      (_, ct) => release.Task.WaitAsync(ct)
    );
    context.AddAuthenticationServices();
    await context
      .Services.GetRequiredService<TokenStorageService>()
      .SetTokensAsync("access", "refresh");
    var navigation = context.Services.GetRequiredService<NavigationManager>();
    navigation.NavigateTo("/login");
    var component = context.Render<ObservedLogin>();
    var initialized = component.Instance.Initialized;
    Assert.Empty(component.FindAll("form"));
    if (dispose)
      await component.InvokeAsync(component.Instance.Dispose);
    else
      navigation.NavigateTo("/settings");
    release.SetResult(Profile());
    await initialized.WaitAsync(TimeSpan.FromSeconds(5));
    Assert.EndsWith(dispose ? "/login" : "/settings", navigation.Uri);
  }

  private static HttpResponseMessage Profile() =>
    new(HttpStatusCode.OK)
    {
      Content = JsonContent.Create(
        new
        {
          id = Guid.NewGuid(),
          name = "Dispatcher",
          email = "dispatcher@example.test",
          isAdmin = false,
        }
      ),
    };

  public sealed class ObservedLogin : Login
  {
    private readonly TaskCompletionSource _initialized = new(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    public Task Initialized => _initialized.Task;

    protected override async Task OnInitializedAsync()
    {
      try
      {
        await base.OnInitializedAsync();
      }
      finally
      {
        _initialized.TrySetResult();
      }
    }
  }

  [Fact]
  public async Task PendingLoginDisablesSubmissionAndRejectedCredentialsRemainEditable()
  {
    var release = new TaskCompletionSource<HttpResponseMessage>(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    await using var context = new ClientComponentContext(
      (_, ct) => release.Task.WaitAsync(ct)
    );
    context.AddAuthenticationServices();
    var component = context.Render<Login>();
    component.Find("#email").Change("dispatcher@example.test");
    component.Find("#password").Change("incorrect-password");
    var submitting = component.Find("form").SubmitAsync(EventArgs.Empty);
    component.WaitForAssertion(
      () =>
        Assert.True(
          component.Find("button[type=submit]").HasAttribute("disabled")
        )
    );
    release.SetResult(new(HttpStatusCode.Unauthorized));
    await submitting;
    Assert.Equal(
      "Invalid email or password.",
      component.Find(".login-page__error").TextContent.Trim()
    );
    Assert.False(
      component.Find("button[type=submit]").HasAttribute("disabled")
    );
    Assert.Equal(
      "dispatcher@example.test",
      component.Find("#email").GetAttribute("value")
    );
    Assert.Null(
      await context
        .Services.GetRequiredService<TokenStorageService>()
        .GetAccessTokenAsync()
    );
  }

  [Fact]
  public async Task ATemporaryFailureCanBeRetriedWithoutLosingTheForm()
  {
    var logins = 0;
    await using var context = new ClientComponentContext(
      (request, _) =>
      {
        if (request.RequestUri!.AbsolutePath == "/api/auth/me")
          return Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
              Content = JsonContent.Create(
                new
                {
                  id = Guid.NewGuid(),
                  name = "Dispatcher",
                  email = "dispatcher@example.test",
                  isAdmin = false,
                }
              ),
            }
          );
        logins++;
        return Task.FromResult(
          logins == 1
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : new HttpResponseMessage(HttpStatusCode.OK)
            {
              Content = JsonContent.Create(
                new { accessToken = "access", refreshToken = "refresh" }
              ),
            }
        );
      }
    );
    context.AddAuthenticationServices();
    var component = context.Render<Login>();
    component.Find("#email").Change("dispatcher@example.test");
    component.Find("#password").Change("valid-password");
    await component.Find("form").SubmitAsync(EventArgs.Empty);
    Assert.Contains(
      "Could not reach the server",
      component.Find(".login-page__error").TextContent
    );
    await component.Find("form").SubmitAsync(EventArgs.Empty);
    Assert.Equal(2, logins);
    Assert.Equal(
      "access",
      await context
        .Services.GetRequiredService<TokenStorageService>()
        .GetAccessTokenAsync()
    );
    Assert.EndsWith(
      "/fleet/map",
      context.Services.GetRequiredService<NavigationManager>().Uri
    );
    Assert.Empty(component.FindAll(".login-page__error"));
  }
}
