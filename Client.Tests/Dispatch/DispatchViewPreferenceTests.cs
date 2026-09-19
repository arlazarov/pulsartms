using System.Net;
using System.Net.Http.Json;
using Bunit;
using Client.Pages.Dispatch;
using Client.Tests.Support;
using Microsoft.JSInterop;

namespace Client.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Component")]
public sealed class DispatchViewPreferenceTests
{
  [Theory]
  [InlineData(null, "2", "Papers", true)]
  [InlineData(null, "1", "Table", true)]
  [InlineData("0", "2", "Cards", false)]
  [InlineData("", "2", "Cards", false)]
  [InlineData(null, "9", "Cards", false)]
  [InlineData(null, null, "Cards", false)]
  public async Task ProductPreferenceMigratesOnlyWhenCanonicalValueIsAbsent(
    string? current,
    string? legacy,
    string expected,
    bool migrated
  )
  {
    await using var context = new ClientComponentContext(
      (request, _) => Task.FromResult(Reply(request.RequestUri!.AbsolutePath))
    );
    context.JSInterop.Mode = JSRuntimeMode.Loose;
    context
      .JSInterop.Setup<string?>(
        "localStorage.getItem",
        "pulsartms.dispatch.view"
      )
      .SetResult(current);
    context
      .JSInterop.Setup<string?>("localStorage.getItem", "amftms.dispatch.view")
      .SetResult(legacy);
    context
      .JSInterop.SetupModule("./js/generated/dispatch/dispatch.js")
      .Setup<bool>("isMobile")
      .SetResult(false);

    var component = context.Render<DispatchList>();

    component.WaitForAssertion(
      () =>
        Assert.Equal(
          expected,
          component
            .Find(".dispatch-view button[aria-pressed='true']")
            .TextContent.Trim()
        )
    );
    var writes = context
      .JSInterop.Invocations.Where(call =>
        call.Identifier == "localStorage.setItem"
      )
      .ToList();
    if (migrated)
    {
      var write = Assert.Single(writes);
      Assert.Equal("pulsartms.dispatch.view", write.Arguments[0]);
      Assert.Equal(legacy, write.Arguments[1]);
    }
    else
      Assert.Empty(writes);
    if (current is not null)
      Assert.DoesNotContain(
        context.JSInterop.Invocations,
        call =>
          call.Identifier == "localStorage.getItem"
          && Equals(call.Arguments[0], "amftms.dispatch.view")
      );

    await component
      .FindAll(".dispatch-view button")
      .Single(button => button.TextContent.Trim() == "Papers")
      .ClickAsync(new());
    Assert.All(
      context.JSInterop.Invocations.Where(call =>
        call.Identifier == "localStorage.setItem"
      ),
      call => Assert.Equal("pulsartms.dispatch.view", call.Arguments[0])
    );
  }

  [Fact]
  public async Task FailedMigrationWriteDoesNotDiscardTheReadableLegacyPreference()
  {
    await using var context = new ClientComponentContext(
      (request, _) => Task.FromResult(Reply(request.RequestUri!.AbsolutePath))
    );
    context.JSInterop.Mode = JSRuntimeMode.Loose;
    context
      .JSInterop.Setup<string?>(
        "localStorage.getItem",
        "pulsartms.dispatch.view"
      )
      .SetResult(null);
    context
      .JSInterop.Setup<string?>("localStorage.getItem", "amftms.dispatch.view")
      .SetResult("2");
    context
      .JSInterop.SetupVoid(
        "localStorage.setItem",
        "pulsartms.dispatch.view",
        "2"
      )
      .SetException(new JSException("Storage quota exceeded"));
    context
      .JSInterop.SetupModule("./js/generated/dispatch/dispatch.js")
      .Setup<bool>("isMobile")
      .SetResult(false);

    var component = context.Render<DispatchList>();

    component.WaitForAssertion(
      () =>
        Assert.Equal(
          "Papers",
          component
            .Find(".dispatch-view button[aria-pressed='true']")
            .TextContent.Trim()
        )
    );
    Assert.Single(
      context.JSInterop.Invocations,
      call => call.Identifier == "localStorage.setItem"
    );
  }

  private static HttpResponseMessage Reply(string path) =>
    new(HttpStatusCode.OK)
    {
      Content =
        path == "/api/dispatch/board"
          ? JsonContent.Create(
            new
            {
              success = true,
              response = new
              {
                items = Array.Empty<object>(),
                page = 1,
                totalCount = 0,
              },
            }
          )
        : path == "/api/fleet/locations"
          ? JsonContent.Create(
            new
            {
              success = true,
              response = new
              {
                trucks = Array.Empty<object>(),
                points = Array.Empty<object>(),
              },
            }
          )
        : JsonContent.Create(
          new { success = true, response = Array.Empty<object>() }
        ),
    };
}
