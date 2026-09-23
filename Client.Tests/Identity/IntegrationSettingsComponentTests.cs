using System.Net;
using System.Net.Http.Json;
using Bunit;
using Client.Models.DTO;
using Client.Models.DTO.Integrations;
using Client.Pages.Settings;
using Client.Tests.Support;
using static Client.Tests.Support.IntegrationSettingsFixture;

namespace Client.Tests.Identity;

[Trait("Category", "Identity")]
[Trait("Kind", "Component")]
public sealed class IntegrationSettingsComponentTests
{
  [Fact]
  public async Task OnlyTheApprovedProvidersAppearWithoutCredentialReadback()
  {
    using var context = new ClientComponentContext(
      (request, _) =>
      {
        Assert.Equal(HttpMethod.Get, request.Method);
        return Task.FromResult(
          request.RequestUri!.AbsolutePath switch
          {
            "/api/settings/integrations" => ListResponse(),
            "/api/settings/integrations/whatsapp/webhook" =>
              IntegrationSettingsFixture.Response(
                new WhatsAppWebhookAddress("api/webhooks/whatsapp/amfcarrier")
              ),
            var path => throw new InvalidOperationException(path),
          }
        );
      }
    );
    var component = context.Render<IntegrationSettings>();
    component.WaitForAssertion(
      () =>
        Assert.Equal(
          4,
          component.FindAll(".integration-settings__status.is-configured").Count
        )
    );
    Assert.Equal(
      new[] { "torqueai", "samsara", "google-email", "whatsapp" },
      component
        .FindAll("[data-provider]")
        .Select(element => element.GetAttribute("data-provider"))
    );
    Assert.Contains(
      "not that the connection has been tested",
      component.Markup
    );
    Assert.DoesNotContain("Connected", component.Markup);
    Assert.Empty(component.FindAll("input"));
    Assert.Empty(component.FindAll(".integration-settings .btn--text"));
    Assert.Contains(
      "/api/webhooks/whatsapp/amfcarrier",
      component.Find("[data-provider='whatsapp'] code").TextContent
    );
    foreach (
      var provider in new[]
      {
        "torqueai",
        "samsara",
        "google-email",
        "whatsapp",
      }
    )
      await component
        .Find($"[data-provider='{provider}'] button")
        .ClickAsync(new());
    var fields = component.FindAll("input");
    Assert.Equal(9, fields.Count);
    Assert.All(
      fields,
      field =>
      {
        Assert.Equal("password", field.GetAttribute("type"));
        Assert.Equal("new-password", field.GetAttribute("autocomplete"));
        Assert.True(string.IsNullOrEmpty(field.GetAttribute("value")));
      }
    );
    Assert.Empty(component.FindAll("#integration-google-email-apiKey"));
    Assert.Empty(context.JSInterop.Invocations);
  }

  [Fact]
  public async Task SavingOneProviderLeavesOtherDraftsUntouchedAndNeverSendsBlankFields()
  {
    var writes = new List<(string Path, IntegrationCredentialsUpdate Body)>();
    using var context = new ClientComponentContext(
      async (request, ct) =>
      {
        if (request.Method == HttpMethod.Get)
          return ListResponse();
        var body = (
          await request.Content!.ReadFromJsonAsync<IntegrationCredentialsUpdate>(
            ct
          )
        )!;
        writes.Add((request.RequestUri!.AbsolutePath, body));
        return Response(State("google-email", 4, true));
      }
    );
    var component = context.Render<IntegrationSettings>();
    await component
      .WaitForElement("[data-provider='torqueai'] button")
      .ClickAsync(new());
    component.Find("#integration-torqueai-apiKey").Input("torque-draft");
    await component
      .Find("[data-provider='google-email'] button")
      .ClickAsync(new());
    component
      .Find("#integration-google-email-refreshToken")
      .Input("replacement-token");
    await component
      .Find("[data-provider='google-email'] form")
      .SubmitAsync(EventArgs.Empty);

    var write = Assert.Single(writes);
    Assert.Equal("/api/settings/integrations/google-email", write.Path);
    Assert.Equal(3, write.Body.Revision);
    Assert.False(write.Body.RestoreDeployment);
    Assert.Equal("replacement-token", Assert.Single(write.Body.Fields).Value);
    Assert.True(write.Body.Fields.ContainsKey("refreshToken"));
    Assert.Equal(
      "torque-draft",
      component.Find("#integration-torqueai-apiKey").GetAttribute("value")
    );
    Assert.Empty(component.FindAll("[data-provider='google-email'] input"));
    await component
      .Find("[data-provider='google-email'] button")
      .ClickAsync(new());
    Assert.All(
      component.FindAll("[data-provider='google-email'] input"),
      field => Assert.True(string.IsNullOrEmpty(field.GetAttribute("value")))
    );
  }

  [Fact]
  public async Task BlankDraftDoesNotWriteAndCancelClearsEnteredSecrets()
  {
    var writes = 0;
    using var context = new ClientComponentContext(
      (request, _) =>
      {
        if (request.Method != HttpMethod.Get)
          writes++;
        return Task.FromResult(ListResponse());
      }
    );
    var component = context.Render<IntegrationSettings>();
    await component
      .WaitForElement("[data-provider='samsara'] button")
      .ClickAsync(new());
    component.Find("#integration-samsara-apiKey").Input("   ");
    await component
      .Find("[data-provider='samsara'] form")
      .SubmitAsync(EventArgs.Empty);
    Assert.Equal(0, writes);
    component.Find("#integration-samsara-apiKey").Input("discard-me");
    await component
      .Find("[data-provider='samsara'] form button[type=button]")
      .ClickAsync(new());
    Assert.DoesNotContain("discard-me", component.Markup);
    await component.Find("[data-provider='samsara'] button").ClickAsync(new());
    Assert.True(
      string.IsNullOrEmpty(
        component.Find("#integration-samsara-apiKey").GetAttribute("value")
      )
    );
    Assert.Equal(0, writes);
  }

  [Fact]
  public async Task FailurePreservesDraftButDoesNotEchoRawServerErrors()
  {
    using var context = new ClientComponentContext(
      (request, _) =>
        Task.FromResult(
          request.Method == HttpMethod.Get
            ? ListResponse()
            : Failure(
              HttpStatusCode.InternalServerError,
              "DO_NOT_DISPLAY_RAW_PAYLOAD"
            )
        )
    );
    var component = context.Render<IntegrationSettings>();
    await component
      .WaitForElement("[data-provider='torqueai'] button")
      .ClickAsync(new());
    component.Find("#integration-torqueai-apiKey").Input("retry-value");
    await component
      .Find("[data-provider='torqueai'] form")
      .SubmitAsync(EventArgs.Empty);
    Assert.Equal(
      "retry-value",
      component.Find("#integration-torqueai-apiKey").GetAttribute("value")
    );
    Assert.Contains(
      "Your entries are unchanged",
      component.Find("[role=alert]").TextContent
    );
    Assert.DoesNotContain("DO_NOT_DISPLAY_RAW_PAYLOAD", component.Markup);
    Assert.Empty(component.FindAll(".settings-page__saved"));
  }

  [Fact]
  public async Task ConflictRefreshesOnlyThatRevisionWhilePreservingDrafts()
  {
    var reads = 0;
    var revisions = new List<long>();
    using var context = new ClientComponentContext(
      async (request, ct) =>
      {
        if (request.Method == HttpMethod.Get)
          return ListResponse(++reads == 1 ? 3 : 7);
        revisions.Add(
          (
            await request.Content!.ReadFromJsonAsync<IntegrationCredentialsUpdate>(
              ct
            )
          )!.Revision
        );
        return revisions.Count == 1
          ? Failure(HttpStatusCode.Conflict, "conflict")
          : Response(State("torqueai", 8, true));
      }
    );
    var component = context.Render<IntegrationSettings>();
    await component
      .WaitForElement("[data-provider='torqueai'] button")
      .ClickAsync(new());
    component.Find("#integration-torqueai-apiKey").Input("retained-value");
    await component.Find("[data-provider='samsara'] button").ClickAsync(new());
    component.Find("#integration-samsara-apiKey").Input("other-draft");
    await component
      .Find("[data-provider='torqueai'] form")
      .SubmitAsync(EventArgs.Empty);
    Assert.True(
      component
        .Find("[data-provider='torqueai'] button[type=submit]")
        .HasAttribute("disabled")
    );
    await component
      .Find("[data-provider='torqueai'] [role=alert] button")
      .ClickAsync(new());
    Assert.Equal(
      "retained-value",
      component.Find("#integration-torqueai-apiKey").GetAttribute("value")
    );
    Assert.Equal(
      "other-draft",
      component.Find("#integration-samsara-apiKey").GetAttribute("value")
    );
    await component
      .Find("[data-provider='torqueai'] form")
      .SubmitAsync(EventArgs.Empty);
    Assert.Equal(new long[] { 3, 7 }, revisions);
  }

  [Fact]
  public async Task CancellingAConflictClearsTheDraftWithoutLosingTheRefreshAction()
  {
    using var context = new ClientComponentContext(
      (request, _) =>
        Task.FromResult(
          request.Method == HttpMethod.Get
            ? ListResponse()
            : Failure(HttpStatusCode.Conflict, "conflict")
        )
    );
    var component = context.Render<IntegrationSettings>();
    await component
      .WaitForElement("[data-provider='torqueai'] button")
      .ClickAsync(new());
    component
      .Find("#integration-torqueai-apiKey")
      .Input("discard-conflict-draft");
    await component
      .Find("[data-provider='torqueai'] form")
      .SubmitAsync(EventArgs.Empty);
    await component
      .Find("[data-provider='torqueai'] form button[type=button]")
      .ClickAsync(new());
    Assert.Empty(component.FindAll("[data-provider='torqueai'] input"));
    Assert.Contains(
      "Refresh status",
      component.Find("[data-provider='torqueai'] [role=alert]").TextContent
    );
    await component
      .Find("[data-provider='torqueai'] .settings-page__actions button")
      .ClickAsync(new());
    Assert.True(
      string.IsNullOrEmpty(
        component.Find("#integration-torqueai-apiKey").GetAttribute("value")
      )
    );
    Assert.True(
      component
        .Find("[data-provider='torqueai'] button[type=submit]")
        .HasAttribute("disabled")
    );
    await component
      .Find("[data-provider='torqueai'] [role=alert] button")
      .ClickAsync(new());
    component.Find("#integration-torqueai-apiKey").Input("new-draft");
    Assert.False(
      component
        .Find("[data-provider='torqueai'] button[type=submit]")
        .HasAttribute("disabled")
    );
  }

  [Fact]
  public async Task DisposalCancelsAPendingRequestAndClearsAllCredentialDrafts()
  {
    var started = new TaskCompletionSource<CancellationToken>(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    var release = new TaskCompletionSource<HttpResponseMessage>(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    using var context = new ClientComponentContext(
      async (request, ct) =>
      {
        if (request.Method == HttpMethod.Get)
          return ListResponse();
        started.TrySetResult(ct);
        return await release.Task.WaitAsync(ct);
      }
    );
    var component = context.Render<IntegrationSettings>();
    await component
      .WaitForElement("[data-provider='torqueai'] button")
      .ClickAsync(new());
    component.Find("#integration-torqueai-apiKey").Input("pending-secret");
    await component.Find("[data-provider='samsara'] button").ClickAsync(new());
    component.Find("#integration-samsara-apiKey").Input("unsaved-secret");
    var pending = component
      .Find("[data-provider='torqueai'] form")
      .SubmitAsync(EventArgs.Empty);
    var cancellation = await started.Task;
    await component.InvokeAsync(component.Instance.Dispose);
    await pending;
    Assert.True(cancellation.IsCancellationRequested);
    Assert.Empty(component.FindAll(".settings-page__saved"));
    Assert.All(
      component.FindAll("input"),
      field => Assert.True(string.IsNullOrEmpty(field.GetAttribute("value")))
    );
  }

  [Fact]
  public async Task PendingSaveDisablesOnlyItsOwnFieldsAndRejectsDuplicateSubmissions()
  {
    var started = new TaskCompletionSource(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    var release = new TaskCompletionSource<HttpResponseMessage>(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    var torqueWrites = 0;
    var samsaraWrites = 0;
    using var context = new ClientComponentContext(
      async (request, ct) =>
      {
        if (request.Method == HttpMethod.Get)
          return ListResponse();
        if (
          request.RequestUri!.AbsolutePath.EndsWith(
            "torqueai",
            StringComparison.Ordinal
          )
        )
        {
          torqueWrites++;
          started.TrySetResult();
          return await release.Task.WaitAsync(ct);
        }
        samsaraWrites++;
        return Response(State("samsara", 4, true));
      }
    );
    var component = context.Render<IntegrationSettings>();
    await component
      .WaitForElement("[data-provider='torqueai'] button")
      .ClickAsync(new());
    component.Find("#integration-torqueai-apiKey").Input("pending-value");
    var pending = component
      .Find("[data-provider='torqueai'] form")
      .SubmitAsync(EventArgs.Empty);
    await started.Task;
    component.WaitForAssertion(
      () =>
        Assert.True(
          component
            .Find("[data-provider='torqueai'] fieldset")
            .HasAttribute("disabled")
        )
    );
    await component
      .Find("[data-provider='torqueai'] form")
      .SubmitAsync(EventArgs.Empty);
    Assert.Equal(1, torqueWrites);
    await component.Find("[data-provider='samsara'] button").ClickAsync(new());
    component.Find("#integration-samsara-apiKey").Input("independent-value");
    await component
      .Find("[data-provider='samsara'] form")
      .SubmitAsync(EventArgs.Empty);
    Assert.Equal(1, samsaraWrites);
    Assert.Equal(
      "pending-value",
      component.Find("#integration-torqueai-apiKey").GetAttribute("value")
    );
    release.SetResult(Response(State("torqueai", 4, true)));
    await pending;
    Assert.Empty(component.FindAll("[data-provider='torqueai'] input"));
  }

  [Fact]
  public async Task ServerConfigurationRequiresConfirmationAndOnlyRestoresOneProvider()
  {
    IntegrationCredentialsUpdate? saved = null;
    using var context = new ClientComponentContext(
      async (request, ct) =>
      {
        if (request.Method == HttpMethod.Get)
          return ListResponse(saved: true);
        Assert.Equal(
          "/api/settings/integrations/torqueai",
          request.RequestUri!.AbsolutePath
        );
        saved =
          await request.Content!.ReadFromJsonAsync<IntegrationCredentialsUpdate>(
            ct
          );
        return Response(State("torqueai", 4));
      }
    );
    var component = context.Render<IntegrationSettings>();
    await component
      .WaitForElement("[data-provider='torqueai'] .btn--text")
      .ClickAsync(new());
    Assert.Null(saved);
    await component
      .Find("[data-provider='torqueai'] [role=group] .btn:not(.btn--primary)")
      .ClickAsync(new());
    Assert.Null(saved);
    await component
      .Find("[data-provider='torqueai'] .btn--text")
      .ClickAsync(new());
    await component
      .Find("[data-provider='torqueai'] [role=group] .btn--primary")
      .ClickAsync(new());
    Assert.NotNull(saved);
    Assert.True(saved.RestoreDeployment);
    Assert.Equal(3, saved.Revision);
    Assert.Empty(saved.Fields);
    Assert.Empty(component.FindAll("[data-provider='torqueai'] .btn--text"));
    Assert.NotEmpty(component.FindAll("[data-provider='samsara'] .btn--text"));
  }

  [Theory]
  [InlineData(HttpStatusCode.Unauthorized, "session has expired")]
  [InlineData(HttpStatusCode.Forbidden, "Only administrators")]
  public void AuthenticationFailuresRemainActionableWithoutExposingEditing(
    HttpStatusCode status,
    string message
  )
  {
    using var context = new ClientComponentContext(
      (_, _) => Task.FromResult(Failure(status, "untrusted"))
    );
    var component = context.Render<IntegrationSettings>();
    Assert.Contains(
      message,
      component.WaitForElement("[role=alert]").TextContent
    );
    Assert.Empty(
      component.FindAll("[data-provider] button, [data-provider] input")
    );
  }

  private static HttpResponseMessage Failure(
    HttpStatusCode status,
    string error
  ) =>
    new(status)
    {
      Content = JsonContent.Create(
        new RequestResponseDTO<IntegrationConnectionState>
        {
          Success = false,
          Errors = [error],
        }
      ),
    };
}
