using System.Net;
using System.Net.Http.Json;
using Bunit;
using Client.Models.DTO;
using Client.Pages.Settings;
using Client.Tests.Support;
using Microsoft.AspNetCore.Components;

namespace Client.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Component")]
public sealed class DispatchNumberSettingsTests
{
  [Theory]
  [InlineData("", "")]
  [InlineData("   ", "")]
  [InlineData(" TMS- ", "TMS-")]
  public async Task SavingUsesTheRevisionAndPreservesExplicitEmptyPrefix(
    string input,
    string expected
  )
  {
    var writes = new List<DispatchSettingsUpdate>();
    var published = new List<DispatchSettingsState>();
    using var context = new ClientComponentContext(
      async (request, ct) =>
      {
        Assert.Equal(
          "/api/settings/dispatch",
          request.RequestUri!.AbsolutePath
        );
        if (request.Method == HttpMethod.Get)
          return Response(new("AMF", 3, null));
        writes.Add(
          (
            await request.Content!.ReadFromJsonAsync<DispatchSettingsUpdate>(ct)
          )!
        );
        return Response(new(writes[^1].LoadNumberPrefix, 4, null));
      }
    );
    var component = context.Render<
      CascadingValue<Action<DispatchSettingsState>>
    >(parameters =>
      parameters
        .Add(value => value.Name, "DispatchSettingsChanged")
        .Add(value => value.Value, published.Add)
        .AddChildContent<DispatchNumberSettings>()
    );
    component.WaitForElement("#settings-load-prefix").Change(input);
    await component.Find("form").SubmitAsync(EventArgs.Empty);

    Assert.Equal(new(expected, 3), Assert.Single(writes));
    Assert.Equal(expected, published[^1].LoadNumberPrefix);
    Assert.Equal(4, published[^1].Revision);
    Assert.Equal(
      expected,
      component.Find("#settings-load-prefix").GetAttribute("value") ?? ""
    );
    Assert.Contains("Display preferences saved", component.Markup);
  }

  [Fact]
  public async Task CompanyFormNeverEditsPersonalUnits()
  {
    var writes = new List<DispatchSettingsUpdate>();
    using var context = new ClientComponentContext(
      async (request, ct) =>
      {
        if (request.Method == HttpMethod.Get)
          return Response(new("AMF", 7, null, "fahrenheit", "miles"));
        writes.Add(
          (
            await request.Content!.ReadFromJsonAsync<DispatchSettingsUpdate>(ct)
          )!
        );
        return Response(new("AMF", 8, null, "fahrenheit", "miles"));
      }
    );
    var component = context.Render<DispatchNumberSettings>();
    component.WaitForElement("#settings-load-prefix");
    Assert.Empty(component.FindAll("select"));
    await component.Find("form").SubmitAsync(EventArgs.Empty);
    Assert.Equal(new("AMF", 7), Assert.Single(writes));
    Assert.Single(component.FindAll(".settings-page__saved"));
  }

  [Fact]
  public async Task ConflictPreservesDraftUntilExplicitReload()
  {
    var reads = 0;
    using var context = new ClientComponentContext(
      (request, _) =>
        Task.FromResult(
          request.Method == HttpMethod.Get
            ? Response(
              ++reads == 1 ? new("AMF", 3, null) : new("OTHER-", 4, null)
            )
            : new HttpResponseMessage(HttpStatusCode.Conflict)
            {
              Content = JsonContent.Create(
                new RequestResponseDTO<DispatchSettingsState>
                {
                  Success = false,
                  Errors = ["Settings changed. Reload before saving."],
                }
              ),
            }
        )
    );
    var component = context.Render<DispatchNumberSettings>();
    component.WaitForElement("#settings-load-prefix").Change("DRAFT-");
    await component.Find("form").SubmitAsync(EventArgs.Empty);
    Assert.Equal(
      "DRAFT-",
      component.Find("#settings-load-prefix").GetAttribute("value")
    );
    Assert.Empty(component.FindAll(".settings-page__saved"));
    await component.Find("[role=alert] button").ClickAsync(new());
    Assert.Equal(
      "OTHER-",
      component.Find("#settings-load-prefix").GetAttribute("value")
    );
  }

  [Fact]
  public async Task InvalidLengthDoesNotWrite()
  {
    var writes = 0;
    using var context = new ClientComponentContext(
      (request, _) =>
      {
        if (request.Method != HttpMethod.Get)
          writes++;
        return Task.FromResult(Response(new("AMF", 0, null)));
      }
    );
    var component = context.Render<DispatchNumberSettings>();
    component
      .WaitForElement("#settings-load-prefix")
      .Change(new string('A', 17));
    await component.Find("form").SubmitAsync(EventArgs.Empty);
    Assert.Equal(0, writes);
    Assert.Contains("Use at most 16 characters", component.Markup);
  }

  internal static HttpResponseMessage Response(DispatchSettingsState state) =>
    new(HttpStatusCode.OK)
    {
      Content = JsonContent.Create(
        new RequestResponseDTO<DispatchSettingsState>
        {
          Success = true,
          Response = state,
        }
      ),
    };
}
