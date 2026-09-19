using System.Net;
using System.Net.Http.Json;
using Bunit;
using Client.Layout;
using Client.Shared;
using Client.Shared.Dispatch.LoadNumber;
using Client.Tests.Support;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Client.Tests.Identity;

[Trait("Category", "Identity")]
[Trait("Kind", "Component")]
public sealed class PageTransitionTests
{
  [Fact]
  public void OnlyPathChangesRestartTheRetainedContentAnimation()
  {
    var reads = 0;
    using var context = new ClientComponentContext(
      (_, _) =>
      {
        reads++;
        return Task.FromResult(SettingsResponse());
      }
    );
    context.ComponentFactories.AddStub<Sidebar>();
    var navigation = context.Services.GetRequiredService<NavigationManager>();
    navigation.NavigateTo("/fleet/map");
    var component = context.Render<MainLayout>(p => p.Add(x => x.Body, Body));
    var child = component.FindComponent<LoadNumber>().Instance;
    Assert.Null(Phase());

    Navigate("/dispatch");
    Assert.Equal("a", Phase());
    component.Render();
    Assert.Equal("a", Phase());
    Navigate("/dispatch?search=11006#loads");
    Assert.Equal("a", Phase());
    Navigate("/dispatch?search=11007");
    Assert.Equal("a", Phase());
    Navigate("/settings");
    Assert.Equal("b", Phase());
    Navigate("/fleet/map");
    Assert.Equal("a", Phase());
    Assert.Same(child, component.FindComponent<LoadNumber>().Instance);
    Assert.Equal(1, reads);

    string? Phase() =>
      component.Find("main").GetAttribute("data-page-transition");

    void Navigate(string path)
    {
      navigation.NavigateTo(path);
      component.Render(p => p.Add(x => x.Body, Body));
    }
  }

  [Fact]
  public async Task DelayedSettingsDoNotAnimateTheInitialPage()
  {
    var pending = new TaskCompletionSource<HttpResponseMessage>(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    using var context = new ClientComponentContext(
      (_, ct) => pending.Task.WaitAsync(ct)
    );
    context.ComponentFactories.AddStub<Sidebar>();
    var component = context.Render<MainLayout>(p => p.Add(x => x.Body, Body));
    Assert.Null(component.Find("main").GetAttribute("data-page-transition"));
    await component.InvokeAsync(() => pending.SetResult(SettingsResponse()));
    component.WaitForAssertion(
      () => Assert.Contains("AMF1373", component.Markup)
    );
    Assert.Null(component.Find("main").GetAttribute("data-page-transition"));
  }

  private static RenderFragment Body =>
    builder =>
    {
      builder.OpenComponent<LoadNumber>(0);
      builder.AddAttribute(1, nameof(LoadNumber.Value), 1373);
      builder.CloseComponent();
    };

  private static HttpResponseMessage SettingsResponse() =>
    new(HttpStatusCode.OK)
    {
      Content = JsonContent.Create(
        new
        {
          success = true,
          response = new { loadNumberPrefix = "AMF", revision = 1 },
        }
      ),
    };
}
