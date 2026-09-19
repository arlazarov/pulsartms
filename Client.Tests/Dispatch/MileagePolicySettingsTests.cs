using System.Net;
using System.Net.Http.Json;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using Bunit;
using Client.Models.DTO.Mileage;
using Client.Pages.Settings;
using Client.Tests.Support;

namespace Client.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Component")]
public sealed class MileagePolicySettingsTests
{
  [Fact]
  public async Task EditAndCancelDoNotWriteAndSaveUsesConfirmedRevision()
  {
    var writes = new List<MileagePolicyUpdate>();
    using var context = new ClientComponentContext(
      async (request, ct) =>
      {
        if (request.Method == HttpMethod.Get)
          return MileageComponentResponses.Ok(
            MileageComponentResponses.Policy()
          );
        var update =
          await request.Content!.ReadFromJsonAsync<MileagePolicyUpdate>(ct);
        writes.Add(update!);
        return MileageComponentResponses.Ok(
          MileageComponentResponses.Policy(8, update!.Home)
        );
      }
    );
    var component = context.Render<MileagePolicySettings>();
    var home = component.WaitForElement("#mileage-home");
    Assert.True(home.HasAttribute("disabled"));

    await Button(component, "Edit policy").ClickAsync(new());
    component.Find("#mileage-home").Change("next");
    await Button(component, "Cancel").ClickAsync(new());
    Assert.Empty(writes);
    Assert.Equal("unallocated", Home(component));
    Assert.True(component.Find("#mileage-home").HasAttribute("disabled"));

    await Button(component, "Edit policy").ClickAsync(new());
    component.Find("#mileage-home").Change("next");
    await component.Find("form").SubmitAsync(EventArgs.Empty);
    Assert.Equal(7, Assert.Single(writes).Revision);
    Assert.Equal("next", writes[0].Home);
    Assert.Equal("next", Home(component));
    component.WaitForElement(".settings-page__saved");
  }

  [Fact]
  public async Task InitialFailureOffersRetryWithoutMountingDefaultPolicy()
  {
    var reads = 0;
    using var context = new ClientComponentContext(
      (request, _) =>
      {
        Assert.Equal(HttpMethod.Get, request.Method);
        return Task.FromResult(
          ++reads == 1
            ? MileageComponentResponses.Error<MileagePolicyState>(
              HttpStatusCode.ServiceUnavailable,
              "Policy read unavailable."
            )
            : MileageComponentResponses.Ok(MileageComponentResponses.Policy())
        );
      }
    );
    var component = context.Render<MileagePolicySettings>();
    component.WaitForElement("[role=alert]");
    Assert.Empty(component.FindAll("select"));
    await Button(component, "Reload policy").ClickAsync(new());
    component.WaitForElement("#mileage-home");
    Assert.Equal(2, reads);
    Assert.Empty(component.FindAll("[role=alert]"));
  }

  [Fact]
  public async Task ConflictRetainsDraftUntilExplicitReload()
  {
    var reads = 0;
    var writes = new List<MileagePolicyUpdate>();
    using var context = new ClientComponentContext(
      async (request, ct) =>
      {
        if (request.Method == HttpMethod.Get)
          return MileageComponentResponses.Ok(
            ++reads == 1
              ? MileageComponentResponses.Policy()
              : MileageComponentResponses.Policy(9, "previous")
          );
        writes.Add(
          (await request.Content!.ReadFromJsonAsync<MileagePolicyUpdate>(ct))!
        );
        return MileageComponentResponses.Error<MileagePolicyState>(
          HttpStatusCode.Conflict,
          "Policy changed. Reload before saving."
        );
      }
    );
    var component = context.Render<MileagePolicySettings>();
    component.WaitForElement("#mileage-home");
    await Button(component, "Edit policy").ClickAsync(new());
    component.Find("#mileage-home").Change("next");
    await component.Find("form").SubmitAsync(EventArgs.Empty);
    Assert.Equal("next", Home(component));
    Assert.Empty(component.FindAll(".settings-page__saved"));
    await Button(component, "Reload policy").ClickAsync(new());
    Assert.Equal("previous", Home(component));
    Assert.Equal(7, Assert.Single(writes).Revision);
  }

  [Fact]
  public async Task PendingSaveCannotSubmitTwice()
  {
    var writes = 0;
    var pending = new TaskCompletionSource<HttpResponseMessage>(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    using var context = new ClientComponentContext(
      (request, _) =>
      {
        if (request.Method == HttpMethod.Get)
          return Task.FromResult(
            MileageComponentResponses.Ok(MileageComponentResponses.Policy())
          );
        writes++;
        return pending.Task;
      }
    );
    var component = context.Render<MileagePolicySettings>();
    component.WaitForElement("#mileage-home");
    await Button(component, "Edit policy").ClickAsync(new());
    var save = component.Find("form").SubmitAsync(EventArgs.Empty);
    component.WaitForAssertion(() => Assert.Equal(1, writes));
    Assert.True(component.Find("fieldset").HasAttribute("disabled"));
    await component.Find("form").SubmitAsync(EventArgs.Empty);
    Assert.Equal(1, writes);
    pending.SetResult(
      MileageComponentResponses.Ok(MileageComponentResponses.Policy(8))
    );
    await save;
    component.WaitForElement(".settings-page__saved");
  }

  private static IElement Button(
    IRenderedComponent<MileagePolicySettings> component,
    string label
  ) => component.FindAll("button").Single(x => x.TextContent.Trim() == label);

  private static string Home(
    IRenderedComponent<MileagePolicySettings> component
  ) => ((IHtmlSelectElement)component.Find("#mileage-home")).Value ?? "";
}
