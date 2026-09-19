using Bunit;
using Client.Layout;
using Client.Models;
using Client.Models.DTO;
using Client.Pages.Settings;
using Client.Shared;
using Client.Shared.Dispatch;
using Client.Shared.Dispatch.LoadNumber;
using Client.Shared.Measurements.DistanceText;
using Client.Tests.Support;
using Microsoft.AspNetCore.Components;

namespace Client.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Component")]
public sealed class LoadNumberLayoutTests
{
  [Fact]
  public void LayoutReadsPrefixOnceForAllLoadNumbers()
  {
    var reads = 0;
    using var context = new ClientComponentContext(
      (request, _) =>
      {
        reads++;
        Assert.Equal(
          "/api/settings/dispatch",
          request.RequestUri!.AbsolutePath
        );
        return Task.FromResult(
          DispatchNumberSettingsTests.Response(new("TMS-", 1, null))
        );
      }
    );
    context.ComponentFactories.AddStub<Sidebar>();
    var component = context.Render<MainLayout>(parameters =>
      parameters.Add(layout => layout.Body, Numbers)
    );
    component.WaitForAssertion(
      () => Assert.Contains("TMS-1373", component.Markup)
    );
    Assert.Contains("TMS-1376", component.Markup);
    component.Render();
    Assert.Equal(1, reads);
  }

  [Fact]
  public async Task CompanySettingsCannotOverridePersonalUnits()
  {
    var pending = new TaskCompletionSource<HttpResponseMessage>(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    var reads = 0;
    using var context = new ClientComponentContext(
      (request, ct) =>
      {
        if (request.Method != HttpMethod.Get)
          return Task.FromResult(
            DispatchNumberSettingsTests.Response(
              new("AMF", 4, null, "celsius", "kilometers")
            )
          );
        return ++reads == 1
          ? pending.Task.WaitAsync(ct)
          : Task.FromResult(
            DispatchNumberSettingsTests.Response(new("AMF", 3, null))
          );
      }
    );
    context.ComponentFactories.AddStub<Sidebar>();
    RenderFragment body = builder =>
    {
      builder.OpenComponent<DistanceText>(0);
      builder.AddAttribute(1, "Miles", 1877d);
      builder.CloseComponent();
      builder.OpenComponent<DispatchNumberSettings>(2);
      builder.CloseComponent();
    };
    var component = context.Render<CascadingValue<DisplayUnits>>(parameters =>
      parameters
        .Add(x => x.Value, new("fahrenheit", "miles"))
        .AddChildContent<MainLayout>(layout => layout.Add(x => x.Body, body))
    );
    component.WaitForElement("#settings-load-prefix").Change("AMF");
    await component.Find("form").SubmitAsync(EventArgs.Empty);
    await component.InvokeAsync(
      () =>
        pending.SetResult(
          DispatchNumberSettingsTests.Response(new("AMF", 2, null))
        )
    );
    component.WaitForAssertion(
      () =>
        Assert.Equal(
          "1,877\u00a0mi",
          component.FindComponent<DistanceText>().Markup.Trim()
        )
    );
    Assert.Equal(2, reads);
  }

  [Fact]
  public async Task LateLayoutReadCannotUndoASettingsSave()
  {
    var pending = new TaskCompletionSource<HttpResponseMessage>(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    var reads = 0;
    using var context = new ClientComponentContext(
      (request, ct) =>
      {
        if (request.Method != HttpMethod.Get)
          return Task.FromResult(
            DispatchNumberSettingsTests.Response(new("", 4, null))
          );
        return ++reads == 1
          ? pending.Task.WaitAsync(ct)
          : Task.FromResult(
            DispatchNumberSettingsTests.Response(new("AMF", 3, null))
          );
      }
    );
    context.ComponentFactories.AddStub<Sidebar>();
    RenderFragment body = builder =>
    {
      builder.AddContent(0, Numbers);
      builder.OpenComponent<DispatchNumberSettings>(1);
      builder.CloseComponent();
    };
    var component = context.Render<MainLayout>(parameters =>
      parameters.Add(layout => layout.Body, body)
    );
    component.WaitForElement("#settings-load-prefix").Change("");
    await component.Find("form").SubmitAsync(EventArgs.Empty);
    await component.InvokeAsync(
      () =>
        pending.SetResult(
          DispatchNumberSettingsTests.Response(new("OLD-", 2, null))
        )
    );
    component.WaitForAssertion(
      () => Assert.Equal("1373", component.Find(".first-load").TextContent)
    );
    Assert.Equal("1376", component.Find(".second-load").TextContent);
  }

  private static RenderFragment Numbers =>
    builder =>
    {
      builder.OpenElement(0, "span");
      builder.AddAttribute(1, "class", "first-load");
      builder.OpenComponent<LoadNumber>(2);
      builder.AddAttribute(3, nameof(LoadNumber.Value), 1373);
      builder.CloseComponent();
      builder.CloseElement();
      builder.OpenElement(4, "span");
      builder.AddAttribute(5, "class", "second-load");
      builder.OpenComponent<LoadNumber>(6);
      builder.AddAttribute(7, nameof(LoadNumber.Value), 1376);
      builder.CloseComponent();
      builder.CloseElement();
    };
}
